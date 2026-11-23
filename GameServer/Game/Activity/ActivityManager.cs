using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Activity;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Activity.Activities;
using March7thHoney.GameServer.Game.Activity.DiceCombat;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Activity;

public class ActivityManager : BasePlayerManager
{
    public const int TrialFallbackEntryId = PlayerInstance.DefaultSpawnEntryId;

    public ActivityManager(PlayerInstance player) : base(player)
    {
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<ActivityData>(player.Uid);

        // A trial left running at logout is abandoned by RescueTrialAtLogin, not resumed.
        DiceCombat = new DiceCombatActivityInstance(this);
    }

    #region Data

    public ActivityData Data { get; set; }

    #endregion

    #region Instance

    public TrialActivityInstance? TrialActivityInstance { get; set; }
    public DiceCombatActivityInstance DiceCombat { get; set; }
    #endregion

    public List<ActivityScheduleData> ToProto()
    {
        var proto = new List<ActivityScheduleData>();

        foreach (var activity in GameData.ActivityConfig.ScheduleData)
            proto.Add(new ActivityScheduleData
            {
                ActivityId = (uint)activity.ActivityId,
                BeginTime = activity.BeginTime,
                EndTime = activity.EndTime,
                PanelId = (uint)activity.PanelId
            });

        return proto;
    }

    #region Trial Activity

    /// <summary>
    ///     The completed-trial avatar collection (wire field OBOPMFBGBAE): the featured avatar of every cleared
    ///     trial stage, in clear order and without duplicates. Derived from the clear records, so no extra save
    ///     data is needed.
    /// </summary>
    public List<PAALDKHKOLE> GetTrialAvatarProtoList()
    {
        var result = new List<PAALDKHKOLE>();
        var seen = new HashSet<int>();
        foreach (var record in Data.TrialActivityData.Activities)
        {
            if (!GameData.AvatarDemoConfigData.TryGetValue(record.StageId, out var stage)) continue;
            if (stage.AvatarID == 0 || !seen.Add(stage.AvatarID)) continue;
            result.Add(new PAALDKHKOLE
            {
                AvatarId = (uint)stage.AvatarID,
                EnhancedId = (uint)GetOwnedAvatarEnhancedId(stage.AvatarID)
            });
        }

        return result;
    }

    // The enhanced id of the player's own copy of the avatar, on its current path; 0 when the avatar is not owned.
    private int GetOwnedAvatarEnhancedId(int avatarId)
    {
        var avatar = Player.AvatarManager?.GetFormalAvatar(avatarId);
        if (avatar == null) return 0;
        return (avatar.GetPathInfo(avatar.AvatarId) ?? avatar.GetPathInfo(avatar.BaseAvatarId))?.EnhanceId ?? 0;
    }

    /// <summary>
    ///     Abandons a trial left running at logout (the official client reports no running stage after a relogin
    ///     and puts the player back in the world): clears the running stage, quietly drops the trial lineup and
    ///     moves the saved position to the trial's return point. Must run before the login lineup reset and the
    ///     scene load.
    /// </summary>
    public void RescueTrialAtLogin()
    {
        var trial = Data.TrialActivityData;
        GameData.MazePlaneData.TryGetValue(Player.Data.PlaneId, out var savedPlane);
        var inTrialPlane = savedPlane?.PlaneType == PlaneTypeEnum.TrialActivity;
        if (trial.CurTrialStageId == 0 && !inTrialPlane) return;

        var returnEntryId = trial.ReturnEntryId;
        var returnPos = trial.ReturnPos;
        var returnRot = trial.ReturnRot;

        trial.CurTrialStageId = 0;
        trial.ReturnEntryId = 0;
        TrialActivityInstance = null;

        var lineupData = Player.LineupManager?.Data;
        if (lineupData != null)
        {
            var trialLineupIndex = (int)ExtraLineupType.LineupStageTrial + 10;
            lineupData.Lineups.Remove(trialLineupIndex);
            if (lineupData.CurExtraLineup == trialLineupIndex) lineupData.CurExtraLineup = -1;
        }

        if (!(returnEntryId != 0 && TryPlaceAtEntrance(returnEntryId, returnPos, returnRot)) &&
            !TryPlaceAtEntrance(TrialFallbackEntryId, null, null) && inTrialPlane)
            // No usable destination: make the saved plane unloadable so login falls back to its default scene
            // instead of rebuilding an empty trial floor.
            Player.Data.PlaneId = 0;

        DatabaseHelper.MarkDirty(Player.Uid);
    }

    private bool TryPlaceAtEntrance(int entryId, Position? pos, Position? rot)
    {
        return Player.TryPlaceAtEntrance(entryId, pos, rot, PlaneTypeEnum.TrialActivity);
    }

    #endregion
}
