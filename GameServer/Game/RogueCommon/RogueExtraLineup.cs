using March7thHoney.Database.Avatar;
using March7thHoney.Database.Lineup;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.RogueCommon;

/// <summary>Which avatars a fresh rogue lineup refills.</summary>
public enum RogueLineupRefill
{
    /// <summary>The requested ids looked up as owned avatars; a trial avatar in the request keeps its state.</summary>
    RequestedFormalAvatars,

    /// <summary>Every slot of the built lineup, trial avatars included.</summary>
    LineupSlots
}

/// <summary>How a rogue mode seeds the extra lineup of a new run.</summary>
/// <param name="Mp">Technique points the run opens with; null opens at the player's maximum.</param>
/// <param name="Sp">Energy each refilled avatar starts with, out of <see cref="RogueExtraLineup.FullSp" />.</param>
/// <param name="Refill">Which avatars get full HP and <paramref name="Sp" />.</param>
public readonly record struct RogueLineupStart(int? Mp, int Sp, RogueLineupRefill Refill);

/// <summary>The extra lineup a rogue run plays with, shared by every rogue mode.</summary>
public static class RogueExtraLineup
{
    public const int FullHp = 10000;
    public const int FullSp = 10000;
    public const int HalfSp = 5000;

    /// <summary>
    ///     Builds the run's extra lineup from <paramref name="avatarIds" />, selects it and seeds it as
    ///     <paramref name="start" /> describes. False when no avatar made it into the lineup.
    /// </summary>
    public static async ValueTask<bool> Activate(PlayerInstance player, ExtraLineupType type, List<int> avatarIds,
        RogueLineupStart start)
    {
        var lineupManager = player.LineupManager!;
        lineupManager.SetExtraLineup(type, avatarIds);
        var lineup = lineupManager.GetExtraLineup(type);
        if (lineup?.BaseAvatars is not { Count: > 0 } slots) return false;

        // Without a notify, selecting the lineup is an in-memory write that sends nothing, so the seeding below
        // does not depend on running after it.
        await lineupManager.SetExtraLineup(type, false);
        lineup.Mp = start.Mp ?? lineupManager.GetMaxMp();
        foreach (var avatar in RefilledAvatars(player, avatarIds, slots, start.Refill))
        {
            avatar.SetCurHp(FullHp, true);
            avatar.SetCurSp(start.Sp, true);
        }

        return true;
    }

    /// <summary>Returns the player to their own lineup without a notify.</summary>
    public static ValueTask Release(PlayerInstance player)
    {
        return player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, false);
    }

    private static IEnumerable<BaseAvatarInfo> RefilledAvatars(PlayerInstance player, List<int> avatarIds,
        List<LineupAvatarInfo> slots, RogueLineupRefill refill)
    {
        var avatars = player.AvatarManager!;
        return refill == RogueLineupRefill.LineupSlots
            ? slots.Select(slot => slot.SpecialAvatarId > 0
                    ? (BaseAvatarInfo?)avatars.GetTrialAvatar(slot.SpecialAvatarId)
                    : avatars.GetFormalAvatar(slot.BaseAvatarId))
                .OfType<BaseAvatarInfo>()
            : avatarIds.Select(avatars.GetFormalAvatar).OfType<BaseAvatarInfo>();
    }
}
