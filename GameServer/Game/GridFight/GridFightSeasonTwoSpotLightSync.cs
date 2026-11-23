using Google.Protobuf;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Protocol side of the Spotlight (主角光环) and the trait-member grants.
/// </summary>
public sealed partial class GridFightManager
{
    private readonly Dictionary<uint, ByteString> _sentSeasonTwoItems = [];

    private static FMBACDOFCAL BuildSpotLightEntry(GridFightSpotLightState entry) => new()
    {
        IHHNLLAJAGB = entry.SpotLightId,
        UniqueId = entry.UniqueId,
        MDNAGANGNKK = entry.RoleUniqueId,
        ELBPNDIENDL = entry.IsLoss,
        RoleId = entry.RoleId,
    };

    /// <summary>GridFightGameInfo field 1658 (GNOBLNOCEHA): owned spotlights and replace/free-refresh counters.</summary>
    private static GNOBLNOCEHA BuildSpotLightState(GridFightSession session)
    {
        var state = new GNOBLNOCEHA
        {
            GAACGAMLFDN = session.SpotLightReplaceTotal,
            GLKHAHHNPPA = session.SpotLightReplaceUsed,
            GOIIFKKGGBJ = session.SpotLightFreeRefreshTotal,
            JGEHHFCJOCL = session.SpotLightFreeRefreshUsed,
        };
        state.FNNEPMAPOOF.Add(session.SpotLights.Select(BuildSpotLightEntry));
        return state;
    }

    /// <summary>
    ///     GridFightUnlockTraitMember items (GridFightGameItemInfo oneof 1763), the client's only source of extra trait
    ///     members: trait-granting spotlights (descriptor category 202 + spotlight id) and the Aha ultra reward.
    /// </summary>
    private IEnumerable<GridFightGameItemInfo> BuildSeasonTwoTraitItems(GridFightSession session)
    {
        foreach (var (itemId, spotLightId, traitId, roleId) in session.GetSpotLightTraitGrants())
        {
            var unlock = new HEHOBDGMMJM { TraitId = traitId };
            unlock.GridFightAvatarList.Add(roleId);
            yield return new GridFightGameItemInfo
            {
                UniqueId = itemId,
                Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Deconienejg, spotLightId),
                INHGPDPNILL = unlock,
            };
        }
        foreach (var (itemId, traitId, roleIds) in session.GetAhaUltraTraitGrants())
        {
            var unlock = new HEHOBDGMMJM { TraitId = traitId };
            unlock.GridFightAvatarList.Add(roleIds);
            yield return new GridFightGameItemInfo
            {
                UniqueId = itemId,
                Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.HackConsole, GridFightSession.AhaTraitId),
                INHGPDPNILL = unlock,
            };
        }
    }

    private void AppendSeasonTwoItemDiffs(GridFightSyncResultData result, GridFightSession session)
    {
        foreach (var item in BuildSeasonTwoTraitItems(session).ToList())
        {
            var bytes = item.ToByteString();
            if (_sentSeasonTwoItems.TryGetValue(item.UniqueId, out var sent) && sent.Equals(bytes)) continue;
            _sentSeasonTwoItems[item.UniqueId] = bytes;
            result.UpdateDynamicList.Add(new GridFightSyncData { GameDataItemInfo = item });
        }
    }

    private void AppendSpotLightChanges(
        GridFightSyncResultData result,
        GridFightSession session,
        GridFightSpotLightChanges changes)
    {
        // SyncData 1779 -> GridFightStage.UpdateRouteNode: refreshes the node's HasSpotLight (route param 4).
        foreach (var (chapterId, sectionId) in changes.RouteNodes)
            if (Catalog.StandardRoute.FirstOrDefault(node => node.ChapterId == chapterId && node.SectionId == sectionId) is
                { } routeNode)
                result.UpdateDynamicList.Add(new GridFightSyncData
                {
                    KBHOBGHKIBA = new BAAGMMIDHDB { DBIIPEEDBHI = BuildRouteNode(routeNode, session) },
                });
        foreach (var role in changes.UpdatedRoles)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        foreach (var entry in changes.Added)
            result.UpdateDynamicList.Add(new GridFightSyncData { PHNKDPMDONM = BuildSpotLightEntry(entry) });
        foreach (var entry in changes.Updated)
            result.UpdateDynamicList.Add(new GridFightSyncData { DEHEDLNDGND = BuildSpotLightEntry(entry) });
        foreach (var entry in changes.Removed)
            result.UpdateDynamicList.Add(new GridFightSyncData { OHHBBOGFDON = BuildSpotLightEntry(entry) });
        if (!changes.CountersChanged) return;
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            JNIGCDNHHLK = new EMOGKOKPCAC
            {
                GAACGAMLFDN = session.SpotLightReplaceTotal,
                GLKHAHHNPPA = session.SpotLightReplaceUsed,
            },
        });
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            DPJKDMKADCD = new CPOHGANENDF
            {
                GOIIFKKGGBJ = session.SpotLightFreeRefreshTotal,
                JGEHHFCJOCL = session.SpotLightFreeRefreshUsed,
            },
        });
    }

    private static GridFightPendingAction BuildSpotLightPendingAction(
        GridFightSession session,
        GridFightPendingAction action)
    {
        var select = new BDLBHJGNCFM
        {
            GFGJOKJGGCO = session.SpotLightTargetUniqueId,
            BKOGIFHEKHH = session.SpotLightGrantRoleId != 0,
            GFJDGNKAOFK = session.SpotLightRerollCostNow,
            DLIANLGFMBG = session.SpotLightGrantRoleId,
            DOAHJPPPMGM = session.SpotLightRerollsLeft,
            ACKOKJBBPKH = session.SpotLightTargetUniqueId == 0,
        };
        select.HPKFKBKOADP.Add(session.SpotLightCandidates.Select(id => new FGEOPPMIHJM { IHHNLLAJAGB = id }));
        action.EEKMIAMGHFL = select;
        return action;
    }

    /// <summary>After a spotlight request: entries, counters, trait items, then the pending action or its finish.</summary>
    public GridFightSyncUpdateResultScNotify BuildSpotLightSync(
        GridFightSpotLightChanges? changes,
        uint finishedQueuePosition,
        bool goldChanged = false)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;
        var result = new GridFightSyncResultData();
        if (goldChanged) result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (changes != null) AppendSpotLightChanges(result, session, changes);
        AppendSeasonTwoItemDiffs(result, session);
        AppendSeasonTwoEffectDiffs(result);
        if (finishedQueuePosition == 0 && BuildPendingAction() is { } pending)
        {
            result.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = BuildPendingLockInfo() });
            result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        }
        notify.ResultList.Add(result);
        if (finishedQueuePosition != 0)
        {
            var finish = new GridFightSyncResultData();
            finish.UpdateDynamicList.Add(new GridFightSyncData { FinishPendingActionPos = finishedQueuePosition });
            finish.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
            notify.ResultList.Add(finish);
        }
        return notify;
    }
}
