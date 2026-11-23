using March7thHoney.Data;
using March7thHoney.Database.Avatar;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Avatar;

/// <summary>Trailblazer path and gender handling.</summary>
public partial class AvatarManager
{
    /// <summary>The opening main mission, which holds the nickname and gender prompt.</summary>
    public const int OpeningMainMissionId = 1000101;

    /// <summary>The prologue sub-mission that waits for the nickname and gender choice.</summary>
    public const int NameAndGenderSubMissionId = 100010134;

    public FormalAvatarInfo? GetHero()
    {
        return Data.FormalAvatars.Find(avatar => avatar.BaseAvatarId == TrailblazerAvatar.BaseAvatarId);
    }

    /// <summary>
    ///     Story mode before the gender prompt: the opening mission is running, its name and gender step is not
    ///     finished, and no team holds the Trailblazer. The client keeps every Trailblazer path it is shown for the
    ///     rest of the session, so until the choice the Trailblazer is left out of the avatar data and the choice
    ///     announces it with the chosen path only. A Trailblazer already in a team (a replayed prologue) stays
    ///     visible, since the client cannot build a team member it has no path data for.
    /// </summary>
    public bool IsHeroGenderPending =>
        Player.MissionEnabled &&
        Player.MissionManager!.GetMainMissionStatus(OpeningMainMissionId) == MissionPhaseEnum.Accept &&
        Player.MissionManager.GetSubMissionStatus(NameAndGenderSubMissionId) != MissionPhaseEnum.Finish &&
        !Player.LineupManager!.GetAllLineup().Any(lineup =>
            lineup.BaseAvatars?.Any(avatar => avatar.BaseAvatarId == TrailblazerAvatar.BaseAvatarId) == true);

    /// <summary>
    ///     Corrects the Trailblazer's current path before login (yuukisr_go a532fa0): <c>cur_multi_path_avatar_type</c>
    ///     must be a Trailblazer path variant and, once the gender is known, must match the player's gender. Old saves
    ///     can hold a non-path id or a variant of the opposite gender; the client resolves the team leader from it,
    ///     finds no path data, <c>TeamManager._CreateMemberEntity</c> throws IndexOutOfRange and login hangs on the
    ///     loading screen. Only the in-memory data is fixed and marked dirty, to be persisted by the next save; data
    ///     that is already consistent is left untouched. Returns whether a correction was made.
    /// </summary>
    public bool NormalizeHeroCurPath()
    {
        var hero = GetHero();
        if (hero == null) return false;

        var gender = Player.Data.CurrentGender;
        // Before the story's gender prompt (IsHeroGenderPending) the client has not decided the protagonist's look, so
        // the default gender must not drive a correction.
        var genderKnown = gender is Gender.Man or Gender.Woman && !IsHeroGenderPending;
        var cur = hero.AvatarId;

        if (!TrailblazerAvatar.IsPath(cur))
        {
            // Not a path id: fall back to the lowest-numbered owned path that matches the gender (when known); if there
            // is none, use that gender's Destruction path.
            var owned = hero.PathInfos.Keys.Where(TrailblazerAvatar.IsPath)
                .Where(id => !genderKnown || TrailblazerAvatar.NormalizePathId(id, gender) == id)
                .OrderBy(id => id).ToList();
            cur = owned.Count > 0
                ? owned[0]
                : gender == Gender.Man ? TrailblazerAvatar.MaleDestructionId : TrailblazerAvatar.FemaleDestructionId;
        }
        else if (genderKnown)
        {
            cur = TrailblazerAvatar.NormalizePathId(cur, gender);
        }

        var hasPath = hero.PathInfos.ContainsKey(cur);
        if (cur == hero.AvatarId && Player.Data.CurBasicType == cur && hasPath) return false;
        if (!hasPath && !hero.EnsurePathInfo(cur)) return false;

        Logger.Debug($"Player {Player.Uid}: Trailblazer current path {hero.AvatarId} retargeted to {cur}");
        hero.AvatarId = cur;
        Player.Data.CurBasicType = cur;
        MarkDirty();
        return true;
    }

    /// <summary>
    ///     Switches a multi-path character to <paramref name="type" /> (any variant of the path; Trailblazer ids are
    ///     normalized to the player's gender). Returns the avatar id actually stored, or 0 when the path does not
    ///     belong to that character.
    /// </summary>
    public async ValueTask<int> ChangePathType(int baseAvatarId, MultiPathAvatarTypeEnum type)
    {
        if (!GameData.MultiplePathAvatarConfigData.TryGetValue((int)type, out var pathExcel)) return 0;
        var owner = GetFormalAvatar(baseAvatarId);
        if (owner == null || owner.BaseAvatarId != pathExcel.BaseAvatarID) return 0;
        baseAvatarId = owner.BaseAvatarId;

        FormalAvatarInfo avatar;
        int id;
        if (baseAvatarId == TrailblazerAvatar.BaseAvatarId)
        {
            id = TrailblazerAvatar.NormalizePathId((int)type, Player.Data.CurrentGender);
            avatar = owner;
            // CurBasicType can desync from the hero record (e.g. stale seed data); check both.
            if (Player.Data.CurBasicType == id && avatar.AvatarId == id) return id;
            Player.Data.CurBasicType = id;
            // Set avatar path
            avatar.AvatarId = id;
            avatar.SetCurSp(0, Player.LineupManager!.GetCurLineup()!.IsExtraLineup());
            // Save new skill tree
            avatar.CheckPathSkillTree();
        }
        else
        {
            id = (int)type;
            avatar = owner;
            avatar.AvatarId = id;
            avatar.SetCurSp(0, Player.LineupManager!.GetCurLineup()!.IsExtraLineup());
            // Save new skill tree
            avatar.CheckPathSkillTree();
        }

        // The field actor is built from the current path. Rebuild it before the path notify,
        // or the world keeps the previous model.
        if (Player.SceneInstance != null) await Player.SceneInstance.RefreshAvatarActor(baseAvatarId);

        if (baseAvatarId == TrailblazerAvatar.BaseAvatarId)
        {
            await Player.SendPacket(new PacketAvatarPathChangedNotify(TrailblazerAvatar.BaseAvatarId,
                (MultiPathAvatarType)id));
            await Player.SendPacket(new PacketPlayerSyncScNotify(GetHero()!));
        }
        else
        {
            await Player.SendPacket(new PacketAvatarPathChangedNotify((uint)baseAvatarId, (MultiPathAvatarType)type));
            await Player.SendPacket(new PacketPlayerSyncScNotify(avatar));
        }

        return id;
    }

    /// <summary>
    ///     The gender chosen when the account names its Trailblazer: records it and puts the Trailblazer on that
    ///     gender's Destruction path. Any choice other than female counts as male. When the Trailblazer was still
    ///     hidden (<see cref="IsHeroGenderPending" />), the path is set without a path-change notify and one sync
    ///     introduces the Trailblazer with only the chosen path.
    /// </summary>
    public async ValueTask SetHeroGenderAtCreation(Gender gender)
    {
        var heroHidden = IsHeroGenderPending;
        RecordHeroGender(gender == Gender.Woman ? Gender.Woman : Gender.Man);
        if (!heroHidden)
        {
            await ChangePathType(TrailblazerAvatar.BaseAvatarId, MultiPathAvatarTypeEnum.Warrior);
            return;
        }

        var hero = GetHero();
        if (hero == null) return;
        var pathId = TrailblazerAvatar.NormalizePathId(TrailblazerAvatar.MaleDestructionId, Player.Data.CurrentGender);
        Player.Data.CurBasicType = pathId;
        hero.AvatarId = pathId;
        hero.SetCurSp(0, Player.LineupManager!.GetCurLineup()!.IsExtraLineup());
        hero.CheckPathSkillTree();
        MarkDirty();
        await Player.SendPacket(new PacketPlayerSyncScNotify(hero));
    }

    /// <summary>
    ///     Switches the Trailblazer to <paramref name="gender" />, keeping the current path. Before the story's gender
    ///     prompt only the saved data changes, so the prompt can still introduce the Trailblazer cleanly. Afterwards
    ///     the client keeps the previous gender's path next to the new one until the next login; no known packet
    ///     makes it drop a path.
    /// </summary>
    public async ValueTask SetHeroGender(Gender gender)
    {
        if (gender is not (Gender.Man or Gender.Woman)) return;

        // Keep the current Path; hero ids pair as male(odd)/female(even) per Path.
        var pathBase = Player.Data.CurBasicType;
        if (pathBase is < TrailblazerAvatar.BaseAvatarId or > TrailblazerAvatar.LastPathId)
            pathBase = TrailblazerAvatar.BaseAvatarId;
        if (pathBase % 2 == 0) pathBase -= 1;
        var newId = pathBase + (gender == Gender.Woman ? 1 : 0);
        if (!GameData.MultiplePathAvatarConfigData.ContainsKey(newId))
            newId = gender == Gender.Woman
                ? TrailblazerAvatar.FemaleDestructionId
                : TrailblazerAvatar.MaleDestructionId;

        var heroHidden = IsHeroGenderPending;
        RecordHeroGender(gender);
        Player.Data.CurBasicType = newId;

        var hero = GetHero();
        if (hero != null)
        {
            hero.AvatarId = newId;
            hero.CheckPathSkillTree();
            hero.SetCurSp(0, Player.LineupManager!.GetCurLineup()?.IsExtraLineup() ?? false);
        }

        MarkDirty();
        if (heroHidden) return;

        await Player.SendPacket(new PacketGetBasicInfoScRsp(Player));
        if (hero != null)
        {
            await Player.SendPacket(new PacketAvatarPathChangedNotify(TrailblazerAvatar.BaseAvatarId,
                (MultiPathAvatarType)newId));
            await Player.SendPacket(new PacketPlayerSyncScNotify(hero));
        }

        // Reload in place so the client rebuilds the Trailblazer with the new model.
        if (Player.SceneInstance != null && Player.Data.Pos != null && Player.Data.Rot != null)
            await Player.LoadScene(Player.Data.PlaneId, Player.Data.FloorId, Player.Data.EntryId, Player.Data.Pos,
                Player.Data.Rot, true);
    }

    private void RecordHeroGender(Gender gender)
    {
        Player.Data.CurrentGender = gender;
        Player.Data.IsGenderSet = true;
    }
}
