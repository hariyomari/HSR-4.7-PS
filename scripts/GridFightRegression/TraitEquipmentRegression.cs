using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.Proto;

/// <summary>
///     Stellaron Hunter (星核猎手) Script Equipment ("按羁绊等级解锁并分配"): the pieces follow the bond level, stay
///     wearable, and cannot be used like owned items (craft, reroll, upgrade).
/// </summary>
static class TraitEquipmentRegression
{
    public static void Run(Func<uint?, GridFightManager> newRun, GridFightResourceCatalog catalog,
        Action<string, bool> check)
    {
        if (catalog.CurrentSeason != 2) return;
        var manager = newRun(101);
        var run = manager.Session!;
        if (!run.TraitTemplateIds.ContainsKey(GridFightSession.StellaronHunterTraitId))
        {
            check("Stellaron Hunters: the run rolls a Script Equipment template", false);
            return;
        }
        run.Roles.Clear();
        var members = catalog.GetRolesByTrait(GridFightSession.StellaronHunterTraitId).Take(2).ToList();
        foreach (var member in members)
        {
            run.TryAddRole(member.ID, 1, out var added);
            added.AcquiredRoles.Single().Position = (uint)run.Roles.Count;
        }
        var grantNotify = manager.BuildSeasonTwoFollowUpSync() ?? new GridFightSyncUpdateResultScNotify();
        var granted = run.GetTemplateEquipmentUniqueIds(GridFightSession.StellaronHunterTraitId)
            .Select(id => run.Equipments.FirstOrDefault(item => item.UniqueId == id))
            .ToList();
        check("Stellaron Hunters: an active bond hands out temporary Script Equipment",
            members.Count == 2 && granted.Count > 0 && granted.All(item => item is { IsTemporary: true, Source: 3 }));
        if (granted.Count == 0 || granted[0] is not { } script) return;

        var wearer = run.Roles[0];
        var dressed = run.DressEquipment(wearer.UniqueId, script.UniqueId);
        check("Stellaron Hunters: Script Equipment can be worn", dressed.Success && wearer.EquippedEquipmentUniqueIds.Contains(script.UniqueId));
        // Client GridFightEquipmentInfo.IsTemporary: no reroll, "cannot operate temporary equipment".
        var wire = grantNotify.ResultList.SelectMany(result => result.UpdateDynamicList)
            .Where(data => data.AddGameItemInfo != null)
            .SelectMany(data => data.AddGameItemInfo.GridFightEquipmentList)
            .FirstOrDefault(item => item.UniqueId == script.UniqueId);
        check("Stellaron Hunters: Script Equipment reaches the client as temporary and is not an ordinary item",
            wire is { IODIJBEHEGF: true } && script.IsRestricted);

        // The bond breaks: both members go to the bench.
        foreach (var (role, index) in run.Roles.Select((role, index) => (role, index)))
            role.Position = 14 + (uint)index;
        var notify = manager.BuildSeasonTwoFollowUpSync() ?? new GridFightSyncUpdateResultScNotify();
        var removedIds = notify.ResultList.SelectMany(result => result.UpdateDynamicList)
            .Where(data => data.RemoveGameItemInfo != null)
            .SelectMany(data => data.RemoveGameItemInfo.GridFightEquipmentList)
            .Select(item => item.UniqueId)
            .ToHashSet();
        check("Stellaron Hunters: breaking the bond takes the Script Equipment back, off its wearer too",
            run.Equipments.All(item => item.UniqueId != script.UniqueId) &&
            !wearer.EquippedEquipmentUniqueIds.Contains(script.UniqueId) && removedIds.Contains(script.UniqueId) &&
            run.GetTemplateEquipmentUniqueIds(GridFightSession.StellaronHunterTraitId).Count == 0);

        foreach (var (role, index) in run.Roles.Select((role, index) => (role, index)))
            role.Position = 1 + (uint)index;
        manager.BuildSeasonTwoFollowUpSync();
        check("Stellaron Hunters: restoring the bond hands the Script Equipment out again",
            run.GetTemplateEquipmentUniqueIds(GridFightSession.StellaronHunterTraitId).Count == granted.Count);
    }
}
