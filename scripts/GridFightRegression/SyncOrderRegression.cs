using System.Reflection;
using March7thHoney.Data;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.Proto;

/// <summary>
///     Grants that hand out several roles at once (人才大礼包, orbs, Aha goods...) merge between the roles. A role granted
///     after a merge can take the bench cell that merge emptied, so the sync has to reach the client in the order the
///     server applied it; otherwise the role lands on a cell the client still sees as taken, the cell looks empty and
///     every move into it fails ("消息获取失败"). The client side is replayed by <see cref="ClientBoard" />.
/// </summary>
static class SyncOrderRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Func<uint?, GridFightManager> newRun, GridFightResourceCatalog catalog,
        Action<string, bool> check)
    {
        var plain = catalog.RolePool
            .Where(role => role.Rarity == 1 && catalog.RoleSwitchGroups.All(group => !group.RoleIdList.Contains(role.ID)) &&
                           GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | 2))
            .Select(role => role.ID)
            .Take(2)
            .ToList();
        if (plain.Count < 2)
        {
            check("sync order: two plain 1-cost roles exist", false);
            return;
        }
        var (merged, other) = (plain[0], plain[1]);

        // Two copies on the bench, then one grant of [copy, other]: the copy merges and frees cell 15, the other role
        // takes it.
        var manager = newRun(101);
        var run = manager.Session!;
        run.Roles.Clear();
        run.ForgeItems.Clear();
        run.TryAddRole(merged, 1, out _);
        run.TryAddRole(merged, 1, out _);
        var client = new ClientBoard(run.Roles);
        var apply = typeof(GridFightSession).GetMethod("ApplyAugmentGrant", Private)!;
        var effect = (GridFightAugmentEffectResult)apply.Invoke(run,
            [204801u, new GridFightAugmentGrant { SpecificRoles = [merged, other] }, false])!;
        var otherRole = run.Roles.Single(role => role.RoleId == other);
        check("sync order: a role granted after a merge reuses the cell the merge emptied",
            otherRole.Position == 15 && run.Roles.Single(role => role.RoleId == merged).Star == 2);
        var errors = client.Apply(manager.BuildCommandGrantSync(effect));
        var matches = client.Matches(run.Roles, out var mismatch);
        check("sync order: a multi-role grant reaches the client in the order it was applied",
            errors.Count == 0 && matches || Report(errors, mismatch));

        if (catalog.CurrentSeason == 2) RunAha(catalog, newRun, check);
    }

    /// <summary>Once an Aha is 7-cost, more copies are 1★ 7-cost copies and merge (the follow-up sync carries it).</summary>
    private static void RunAha(GridFightResourceCatalog catalog, Func<uint?, GridFightManager> newRun, Action<string, bool> check)
    {
        // Tier ids follow the tables: the cheapest star-less tier is the one a player buys.
        var ahaBase = catalog.AhaGrowthRoleIds[0];
        var ahaSeven = catalog.AhaSevenCostRoleId;
        var manager = newRun(101);
        var run = manager.Session!;
        run.Roles.Clear();
        run.ForgeItems.Clear();
        run.TryAddRole(ahaSeven, 1, out _);
        run.Roles.Single().Position = 1;
        manager.BuildSeasonTwoFollowUpSync();
        var client = new ClientBoard(run.Roles);
        run.TryAddRole(ahaBase, 1, out var first);
        run.TryAddRole(ahaBase, 1, out var second);
        foreach (var role in first.AcquiredRoles.Concat(second.AcquiredRoles)) client.Add(role);
        var growth = run.AhaGrowth;
        var errors = client.Apply(manager.BuildSeasonTwoFollowUpSync() ?? new GridFightSyncUpdateResultScNotify());
        var ahas = run.Roles.Where(role => role.RoleId == ahaSeven || catalog.AhaGrowthRoleIds.Contains(role.RoleId)).ToList();
        check("Aha: copies bought after the 7-cost apotheosis merge into a 2★ 7-cost Aha",
            ahas.Count == 1 && ahas[0].RoleId == ahaSeven && ahas[0].Star == 2 && run.AhaGrowth == growth);
        var matches = client.Matches(run.Roles, out var mismatch);
        check("Aha: the 7-cost merge reaches the client through the follow-up sync",
            errors.Count == 0 && matches || Report(errors, mismatch));
    }

    private static bool Report(IReadOnlyList<string> errors, string? mismatch)
    {
        foreach (var error in errors) Console.WriteLine($"  client: {error}");
        if (mismatch != null) Console.WriteLine($"  client: {mismatch}");
        return false;
    }

    /// <summary>The client's board as GridFightSyncData updates leave it (unique id -> role id, star, position).</summary>
    private sealed class ClientBoard
    {
        private readonly Dictionary<uint, (uint RoleId, uint Star, uint Pos)> _roles = [];

        public ClientBoard(IEnumerable<GridFightRoleState> roles)
        {
            foreach (var role in roles) Add(role);
        }

        public void Add(GridFightRoleState role) => _roles[role.UniqueId] = (role.RoleId, role.Star, role.Position);

        public List<string> Apply(GridFightSyncUpdateResultScNotify notify)
        {
            var errors = new List<string>();
            foreach (var data in notify.ResultList.SelectMany(result => result.UpdateDynamicList))
            {
                if (data.AddRoleInfo is { } added)
                {
                    if (added.Pos != 0 && _roles.Any(pair => pair.Key != added.UniqueId && pair.Value.Pos == added.Pos))
                        errors.Add($"role {added.UniqueId} added on taken cell {added.Pos}");
                    _roles[added.UniqueId] = (added.Id, added.RoleStar, added.Pos);
                }
                else if (data.UpdateRoleInfo is { } updated)
                {
                    if (!_roles.ContainsKey(updated.UniqueId)) errors.Add($"update of unknown role {updated.UniqueId}");
                    _roles[updated.UniqueId] = (updated.Id, updated.RoleStar, updated.Pos);
                }
                else if (data.MNLCHEJKDHOCase == GridFightSyncData.MNLCHEJKDHOOneofCase.RemoveRoleUniqueId &&
                         !_roles.Remove(data.RemoveRoleUniqueId))
                    errors.Add($"remove of unknown role {data.RemoveRoleUniqueId}");
            }
            return errors;
        }

        public bool Matches(IEnumerable<GridFightRoleState> roles, out string? mismatch)
        {
            var server = roles.ToDictionary(role => role.UniqueId, role => (role.RoleId, role.Star, role.Position));
            mismatch = server.Count == _roles.Count && server.All(pair => _roles.TryGetValue(pair.Key, out var value) && value == pair.Value)
                ? null
                : $"server [{string.Join(", ", server.Select(pair => $"{pair.Key}:{pair.Value}"))}] vs client " +
                  $"[{string.Join(", ", _roles.Select(pair => $"{pair.Key}:{pair.Value}"))}]";
            return mismatch == null;
        }
    }
}
