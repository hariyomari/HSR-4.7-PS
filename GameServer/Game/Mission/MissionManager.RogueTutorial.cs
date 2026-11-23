using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Mission;

public partial class MissionManager
{
    internal bool IsRogueGalleryUnlocked => !Enabled ||
        GetMainMissionStatus(4030002) == MissionPhaseEnum.Finish ||
        GetSubMissionStatus(403000221) == MissionPhaseEnum.Finish;

    internal async ValueTask RefreshRogueGalleryProps()
    {
        var scene = Player.SceneInstance;
        if (scene?.FloorId != 80112001 || !IsRogueGalleryUnlocked) return;
        // The two ChangePropState targets were stripped from Mission_403000222's export.
        // Official 4.6 seq 29410/29411 opens the real arms, not the graph's quest UI dummy.
        foreach (var prop in scene.GetEntitiesInGroup<EntityProp>(3)
                     .Where(prop => prop.Excel.ID == 1005 && prop.InstId is 300003 or 300008))
            if (prop.State != PropStateEnum.Open) await prop.SetState(PropStateEnum.Open);
    }

    /// <summary>Recover the specific story dead end produced by the former tutorial auto-finish path.</summary>
    public async ValueTask<bool> RepairSkippedRogueTutorials()
    {
        if (!Enabled || Data.GetSubMissionStatus(100040129) != MissionPhaseEnum.Accept ||
            Data.GetMainMissionStatus(4030001) != MissionPhaseEnum.Finish ||
            Player.RogueManager!.RogueInstance != null ||
            Player.RogueManager!.RogueData.FirstPassAreas.Contains(100)) return false;

        foreach (var id in new[] { 4030001, 4030002 })
        {
            if (Data.GetMainMissionStatus(id) != MissionPhaseEnum.Finish) continue;
            if (!GameData.MainMissionData.TryGetValue(id, out var main)) continue;
            Data.ReplayedRogueTutorialRewardIds.SafeAdd(id);
            foreach (var subId in main.SubMissionIds)
            {
                Data.SetSubMissionStatus(subId, MissionPhaseEnum.None);
                Data.SubMissionProgressDict.Remove(subId);
            }
            Data.SetMainMissionStatus(id, MissionPhaseEnum.None);
        }

        await AcceptMainMission(4030001, false);
        Data.TrackingMainMissionId = 4030001;
        DatabaseHelper.MarkDirty(Player.Uid);
        Logger.Debug($"uid {Player.Uid}: resumed skipped Rogue tutorial 4030001; previous rewards retained.");
        return true;
    }
}
