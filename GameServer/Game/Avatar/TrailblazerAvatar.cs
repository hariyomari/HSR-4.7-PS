using March7thHoney.Data;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Avatar;

/// <summary>
///     Avatar IDs of the Trailblazer. Every path is a MultiplePathAvatarConfig row whose BaseAvatarID is
///     <see cref="BaseAvatarId" />, and the path variants pair as male (odd) / female (even = odd + 1).
/// </summary>
public static class TrailblazerAvatar
{
    /// <summary>The base avatar of every Trailblazer path; as a path it is the male Destruction Trailblazer.</summary>
    public const int BaseAvatarId = 8001;

    /// <summary>The male Destruction path, the starting path of a male Trailblazer.</summary>
    public const int MaleDestructionId = BaseAvatarId;

    /// <summary>The female Destruction path, the starting path of a female Trailblazer.</summary>
    public const int FemaleDestructionId = 8002;

    /// <summary>The highest Trailblazer path ID: the female variant of Elation (8009).</summary>
    public const int LastPathId = 8010;

    /// <summary>Whether the ID is one of the Trailblazer's paths in MultiplePathAvatarConfig.</summary>
    public static bool IsPath(int avatarId)
    {
        return GameData.MultiplePathAvatarConfigData.TryGetValue(avatarId, out var config) &&
               config.BaseAvatarID == BaseAvatarId;
    }

    /// <summary>
    ///     Maps a Trailblazer path variant to the player's gender. Ids without a matching config entry, and players
    ///     without a gender, are returned unchanged.
    /// </summary>
    public static int NormalizePathId(int pathId, Gender gender)
    {
        if (gender is not (Gender.Man or Gender.Woman)) return pathId;
        var maleId = pathId % 2 == 0 ? pathId - 1 : pathId;
        var id = gender == Gender.Woman ? maleId + 1 : maleId;
        return IsPath(id) ? id : pathId;
    }

    /// <summary>
    ///     Whether <paramref name="pathId" /> is the Destruction, Preservation or Harmony Trailblazer of the gender the
    ///     player does not play. Any gender other than male counts as female here.
    /// </summary>
    public static bool IsOppositeGenderPath(int pathId, Gender gender)
    {
        return gender == Gender.Man ? pathId is 8002 or 8004 or 8006 : pathId is 8001 or 8003 or 8005;
    }

    /// <summary>
    ///     Drops the trial avatars that stand for the Trailblazer of the gender the player does not play and keeps the
    ///     order of the rest. Trial lists name both variants and the stage or mission expects one of them.
    ///     <paramref name="pathOf" /> maps a list entry to the avatar it stands for, or 0 when it is not resolvable.
    /// </summary>
    public static List<int> ExcludeOppositeGenderTrials(IEnumerable<int> trialIds, Gender gender,
        Func<int, int> pathOf)
    {
        return trialIds.Where(id => !IsOppositeGenderPath(pathOf(id), gender)).ToList();
    }

    /// <summary>
    ///     The avatar a trial list entry names by its last four digits. Entries up to 10000 are plain avatars and map
    ///     to 0.
    /// </summary>
    public static int AvatarIdFromTrialDigits(int trialId)
    {
        return trialId > 10000 ? trialId % 10000 : 0;
    }
}
