using March7thHoney.Enums;

namespace March7thHoney.GameServer.Game.Player;

public partial class PlayerInstance
{
    /// <summary>
    ///     What an online player is doing, as reported by the MUIP player-information endpoint. Managers that are not
    ///     initialized yet (before login completes) count as idle rather than as an active mode.
    /// </summary>
    public (PlayerStatusEnum Status, PlayerSubStatusEnum SubStatus) GetOnlineStatus()
    {
        // TODO: a player in a challenge is not reported as such; that check was suspended during the 4.3 migration.
        var status = RaidManager is { } raid && raid.Data.CurRaidId != 0
            ? PlayerStatusEnum.Raid
            : StoryLineManager is { } storyLine && storyLine.StoryLineData.CurStoryLineId != 0
                ? PlayerStatusEnum.StoryLine
                : PlayerStatusEnum.Explore;
        var subStatus = BattleInstance != null ? PlayerSubStatusEnum.Battle : PlayerSubStatusEnum.None;
        return (status, subStatus);
    }
}
