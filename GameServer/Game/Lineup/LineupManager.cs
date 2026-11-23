using March7thHoney.Data.Avatar;
using March7thHoney.GameServer.Game.Friend;
using March7thHoney.GameServer.Server.Packet.Send.Friend;
using March7thHoney.Data;
using March7thHoney.Data.Config.AdventureAbility;
using March7thHoney.Data.Excel;
using March7thHoney.Data.Config.Task;
using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Lineup;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Quest;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;

namespace March7thHoney.GameServer.Game.Lineup;

public class LineupManager : BasePlayerManager<LineupData>
{
    public LineupManager(PlayerInstance player) : base(player)
    {
        foreach (var lineupInfo in Data.Lineups.Values)
        {
            lineupInfo.LineupData = Data;
            lineupInfo.AvatarData = player.AvatarManager!.Data;
        }
        PadNormalLineups();
        EnsurePlayableCurLineup();
        RecalculateExtraMpCount();
    }

    #region Detail

    public LineupInfo? GetLineup(int lineupIndex)
    {
        Data.Lineups.TryGetValue(lineupIndex, out var lineup);
        return lineup;
    }

    public LineupInfo? GetExtraLineup(ExtraLineupType type)
    {
        var index = (int)type + 10;
        Data.Lineups.TryGetValue(index, out var lineup);
        return lineup;
    }

    public LineupInfo? GetCurLineup()
    {
        var lineup = GetLineup(Data.GetCurLineupIndex());
        return lineup;
    }

    public int GetMaxMp()
    {
        return 5 + Data.ExtraMpCount;
    }

    /// <summary>
    ///     Recalculates ExtraMpCount from the current lineup; the bonus comes from AdvModifyMaxMazeMP in each avatar's
    ///     AdventureAbility.
    /// </summary>
    public void RecalculateExtraMpCount()
    {
        var extra = 0;
        var lineup = GetCurLineup();
        foreach (var slot in lineup?.BaseAvatars ?? [])
            extra += ResolveExtraMazeMp(ResolveAvatarId(slot));

        Data.ExtraMpCount = extra;
        if (lineup != null)
            lineup.Mp = Math.Min(Math.Max(0, lineup.Mp), 5 + extra);
    }

    /// <summary>Normalizes a trial avatar to its real avatar and a multi-path avatar to its BaseAvatarID.</summary>
    private static int ResolveAvatarId(LineupAvatarInfo slot)
    {
        var avatarId = slot.BaseAvatarId;
        if (slot.SpecialAvatarId > 0 &&
            GameData.SpecialAvatarData.TryGetValue(slot.SpecialAvatarId, out var special))
            avatarId = special.AvatarID;
        if (GameData.MultiplePathAvatarConfigData.TryGetValue(avatarId, out var multiPath))
            avatarId = multiPath.BaseAvatarID;
        return avatarId;
    }

    /// <summary>
    ///     The bonus to the technique point cap that the avatar's modifiers declare in OnCreate, applied while the
    ///     avatar is in the lineup.
    /// </summary>
    private static int ResolveExtraMazeMp(int avatarId)
    {
        if (!GameData.AvatarConfigData.TryGetValue(avatarId, out var avatar)) return 0;
        if (!GameData.AdventureAbilityConfigListData.TryGetValue(avatar.AdventurePlayerID, out var abilities))
            return 0;

        var extra = 0;
        var modifiers = abilities.GlobalModifiers?.Values ?? Enumerable.Empty<AdventureModifierConfig>();
        foreach (var modifier in modifiers)
        foreach (var task in modifier.OnCreate)
        {
            if (task is not AdvModifyMaxMazeMP modify) continue;
            var value = modify.ModifyValue.GetValue();
            extra = modify.ModifyFunction == PropertyModifyFunctionEnum.Set ? value - 5 : extra + value;
        }
        return extra;
    }

    /// <summary>
    ///     Writes MaxMp into the LineupInfo proto. ToProto can miss ExtraMpCount, so the value is forced before the
    ///     packet is sent.
    /// </summary>
    public void ApplyMaxMp(Proto.LineupInfo proto)
    {
        if (proto == null) return;
        RecalculateExtraMpCount();
        proto.MaxMp = (uint)GetMaxMp();
        if (proto.Mp > proto.MaxMp)
            proto.Mp = proto.MaxMp;
    }

    public List<AvatarLineupData> GetAvatarsFromTeam(int index)
    {
        var lineup = GetLineup(index);
        if (lineup == null) return [];

        var avatarList = new List<AvatarLineupData>();
        foreach (var avatar in lineup.BaseAvatars!)
        {
            var avatarType = AvatarType.AvatarFormalType;
            BaseAvatarInfo? avatarInfo = null;
            if (avatar.SpecialAvatarId > 0)
            {
                avatarInfo = Player.AvatarManager!.GetTrialAvatar(avatar.SpecialAvatarId);
                avatarType = AvatarType.AvatarTrialType;
                // Trial protagonists can resolve to a different gender/base path than their table row.
                // Keep saved/replaced lineup slots consistent with the actual trial used in combat.
                if (avatarInfo != null && avatar.BaseAvatarId != avatarInfo.BaseAvatarId)
                {
                    if (lineup.LeaderAvatarId == avatar.BaseAvatarId) lineup.LeaderAvatarId = avatarInfo.BaseAvatarId;
                    avatar.BaseAvatarId = avatarInfo.BaseAvatarId;
                    MarkDirty();
                }
            }
            else if (avatar.AssistUid > 0)
            {
                var avatarStorage = DatabaseHelper.Instance?.GetInstance<AvatarData>(avatar.AssistUid);
                avatarType = AvatarType.AvatarAssistType;
                if (avatarStorage == null) continue;
                avatarInfo = avatarStorage.FindByAnyId(avatar.BaseAvatarId);
            }
            else
            {
                avatarInfo = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
            }

            if (avatarInfo == null) continue;
            avatarList.Add(new AvatarLineupData(avatarInfo, avatarType));
        }

        return avatarList;
    }

    public List<AvatarLineupData> GetAvatarsFromCurTeam()
    {
        return GetAvatarsFromTeam(Data.GetCurLineupIndex());
    }

    /// <summary>
    ///     Normal lineups 0..8 come first in index order, followed by the extra lineups (keys &gt;= 10) in index order.
    /// </summary>
    public List<LineupInfo> GetAllLineup()
    {
        PadNormalLineups();
        return
        [
            .. Data.Lineups.Where(x => x.Key < GameConstants.MAX_LINEUP_COUNT).OrderBy(x => x.Key)
                .Select(x => x.Value),
            .. Data.Lineups.Where(x => x.Key >= GameConstants.MAX_LINEUP_COUNT).OrderBy(x => x.Key)
                .Select(x => x.Value)
        ];
    }

    /// <summary>
    ///     Pads the normal lineup slots 0..8 by index (yuukisr_go 6bb4fd1 PadNormalLineups). Padding by dictionary
    ///     entry count is not enough: when a save lacks a middle index or holds extra lineups, slots get skipped or an
    ///     index that already exists throws, and the team screen is left with a single team.
    /// </summary>
    public void PadNormalLineups()
    {
        var changed = false;
        for (var i = 0; i < GameConstants.MAX_LINEUP_COUNT; i++)
        {
            if (Data.Lineups.TryGetValue(i, out var existing))
            {
                if (existing.BaseAvatars == null)
                {
                    existing.BaseAvatars = [];
                    changed = true;
                }
                continue;
            }

            changed = true;
            Data.Lineups[i] = new LineupInfo
            {
                Name = "",
                LineupType = 0,
                BaseAvatars = [],
                LineupData = Data,
                AvatarData = Player.AvatarManager!.Data
            };
        }
        if (changed) MarkDirty();
    }

    /// <summary>
    ///     When the current normal lineup is empty (the save points at a slot that was never saved, or at a padded
    ///     empty slot), switches to the lowest-numbered non-empty normal lineup, so the player does not log in with an
    ///     empty lineup for which the client cannot create a leader entity. Nothing changes when no lineup is
    ///     non-empty (a new account).
    /// </summary>
    public void EnsurePlayableCurLineup()
    {
        if (Data.CurExtraLineup != -1) return;
        if (GetLineup(Data.CurLineup) is { BaseAvatars.Count: > 0 }) return;

        foreach (var (index, lineup) in Data.Lineups.Where(x => x.Key < GameConstants.MAX_LINEUP_COUNT)
                     .OrderBy(x => x.Key))
        {
            if (lineup.BaseAvatars is not { Count: > 0 }) continue;
            Data.CurLineup = index;
            MarkDirty();
            return;
        }
    }

    /// <summary>
    ///     MarkPresetLineupCsReq: marks or unmarks a lineup as favourite; only normal lineup indexes are accepted.
    /// </summary>
    public bool SetLineupFavourite(int lineupIndex, bool isFavourite)
    {
        if (lineupIndex is < 0 or >= GameConstants.MAX_LINEUP_COUNT) return false;
        var lineup = GetLineup(lineupIndex);
        if (lineup == null) return false;

        lineup.IsFavourite = isFavourite;
        MarkDirty();
        return true;
    }

    #endregion

    #region Management

    public async ValueTask<bool> SetCurLineup(int lineupIndex)
    {
        if (lineupIndex < 0 || !Data.Lineups.ContainsKey(lineupIndex)) return false;
        if (GetLineup(lineupIndex)!.BaseAvatars!.Count == 0) return false;
        Data.CurLineup = lineupIndex;
        Data.CurExtraLineup = -1;

        Player.SceneInstance?.SyncLineup();
        await Player.SendPacket(new PacketSyncLineupNotify(GetCurLineup()!));

        return true;
    }

    public void SetExtraLineup(ExtraLineupType type, List<int> baseAvatarIds, bool refresh = false)
    {
        if (type == ExtraLineupType.LineupNone)
        {
            // reset lineup
            Data.CurExtraLineup = -1;
            return;
        }

        var index = (int)type + 10;

        // destroy old lineup
        Data.Lineups.Remove(index);

        // create new lineup
        var lineup = new LineupInfo
        {
            Name = "",
            LineupType = (int)type,
            BaseAvatars = [],
            LineupData = Data,
            AvatarData = Player.AvatarManager!.Data
        };

        var worldLevel = type == ExtraLineupType.LineupStageTrial ? 0 : Player.Data.WorldLevel;

        foreach (var avatarId in baseAvatarIds)
        {
            var trial = Player.AvatarManager!.GetTrialAvatar(avatarId, refresh);
            if (trial != null)
            {
                if (GameData.MultiplePathAvatarConfigData.TryGetValue(trial.AvatarId, out var pathExcel) &&
                    pathExcel.Gender != GenderTypeEnum.GENDER_NONE)
                    if (pathExcel.Gender != (GenderTypeEnum)Player.Data.CurrentGender)
                        continue;

                trial.CheckLevel(worldLevel);
                lineup.BaseAvatars!.Add(new LineupAvatarInfo
                    { BaseAvatarId = trial.BaseAvatarId, SpecialAvatarId = trial.SpecialAvatarId });
            }
            else
            {
                lineup.BaseAvatars!.Add(new LineupAvatarInfo { BaseAvatarId = avatarId });
            }
        }

        Data.Lineups.Add(index, lineup);
        Data.CurExtraLineup = index;
    }

    public async ValueTask SetExtraLineup(ExtraLineupType type, bool notify = true)
    {
        if (type == ExtraLineupType.LineupNone)
        {
            // reset lineup
            Data.CurExtraLineup = -1;
            if (notify) await Player.SendPacket(new PacketSyncLineupNotify(GetCurLineup()!));
            return;
        }

        var index = (int)type + 10;

        // get cur extra lineup
        var lineup = GetExtraLineup(type);
        if (lineup == null || lineup.BaseAvatars?.Count == 0) return;

        Data.CurExtraLineup = index;

        // sync
        if (notify) await Player.SendPacket(new PacketSyncLineupNotify(GetCurLineup()!));
    }

    public async ValueTask AddAvatar(int lineupIndex, int avatarId, bool sendPacket = true)
    {
        if (lineupIndex < 0) return;
        Data.Lineups.TryGetValue(lineupIndex, out var lineup);

        if (lineup == null)
        {
            var baseAvatarId = avatarId;
            if (GameData.MultiplePathAvatarConfigData.TryGetValue(baseAvatarId, out var multiPathAvatar))
                baseAvatarId = multiPathAvatar.BaseAvatarID;
            var specialAvatarId = avatarId * 10 + 0;
            GameData.SpecialAvatarData.TryGetValue(specialAvatarId, out var specialAvatar);
            if (specialAvatar != null)
            {
                Player.AvatarManager!.GetTrialAvatar(avatarId)?.CheckLevel(Player.Data.WorldLevel);
                baseAvatarId = specialAvatar.AvatarID;
            }
            else
            {
                // Fall back to the male Trailblazer only when avatarId is not a valid avatar, so that the female
                // Trailblazer and other Trailblazer paths are not forced to 8001.
                if (baseAvatarId >= TrailblazerAvatar.BaseAvatarId &&
                    !GameData.AvatarConfigData.ContainsKey(baseAvatarId))
                    baseAvatarId = TrailblazerAvatar.BaseAvatarId;
            }

            lineup = new LineupInfo
            {
                Name = "",
                LineupType = 0,
                BaseAvatars =
                [
                    new LineupAvatarInfo
                        { BaseAvatarId = baseAvatarId, SpecialAvatarId = specialAvatar?.SpecialAvatarID ?? 0 }
                ],
                LineupData = Data,
                AvatarData = Player.AvatarManager!.Data
            };
            Data.Lineups.Add(lineupIndex, lineup);
        }
        else
        {
            if (lineup.BaseAvatars!.Count >= GameConstants.MAX_LINEUP_AVATAR_COUNT) return;

            var baseAvatarId = avatarId;
            if (GameData.MultiplePathAvatarConfigData.TryGetValue(baseAvatarId, out var multiPathAvatar))
                baseAvatarId = multiPathAvatar.BaseAvatarID;
            var specialAvatarId = avatarId * 10 + 0;
            GameData.SpecialAvatarData.TryGetValue(specialAvatarId, out var specialAvatar);
            if (specialAvatar != null)
            {
                Player.AvatarManager!.GetTrialAvatar(avatarId)?.CheckLevel(Player.Data.WorldLevel);
                baseAvatarId = specialAvatar.AvatarID;
            }
            else
            {
                // Fall back to the male Trailblazer only when avatarId is not a valid avatar, so that the female
                // Trailblazer and other Trailblazer paths are not forced to 8001.
                if (baseAvatarId >= TrailblazerAvatar.BaseAvatarId &&
                    !GameData.AvatarConfigData.ContainsKey(baseAvatarId))
                    baseAvatarId = TrailblazerAvatar.BaseAvatarId;
            }

            lineup.BaseAvatars?.Add(new LineupAvatarInfo
                { BaseAvatarId = baseAvatarId, SpecialAvatarId = specialAvatar?.SpecialAvatarID ?? 0 });
            Data.Lineups[lineupIndex] = lineup;
        }

        MarkDirty();
        if (sendPacket)
        {
            if (lineupIndex == Data.GetCurLineupIndex())
            {
                RecalculateExtraMpCount();
                Player.SceneInstance?.SyncLineup();
            }
            await Player.SendPacket(new PacketSyncLineupNotify(lineup));
        }
    }

    /// <summary>
    ///     Rewrites trial slots saved with a SpecialAvatar row key (special avatar ID * 10 + world level) to the
    ///     special avatar ID, and brings the trial avatars of all trial slots to the player's world level.
    /// </summary>
    public void RefreshTrialAvatarSlots()
    {
        foreach (var info in GetAllLineup().SelectMany(lineupInfo => lineupInfo.BaseAvatars ?? []))
        {
            if (info.SpecialAvatarId > 0 &&
                GameData.SpecialAvatarData.TryGetValue(info.SpecialAvatarId, out var excel))
            {
                info.SpecialAvatarId = excel.SpecialAvatarID;
                Player.AvatarManager!.GetTrialAvatar(excel.SpecialAvatarID)?.CheckLevel(Player.Data.WorldLevel);
            }

            if (info.SpecialAvatarId > 0 &&
                GameData.SpecialAvatarData.TryGetValue(info.SpecialAvatarId * 10 + 0, out var e))
                Player.AvatarManager!.GetTrialAvatar(e.SpecialAvatarID)?.CheckLevel(Player.Data.WorldLevel);
        }
    }

    /// <summary>
    ///     Login-time repair of the saved lineups, before the scene loads: leaves an extra lineup that no running mode
    ///     owns any more, trims oversized lineups, moves trial IDs saved as a base avatar into the trial slot and
    ///     revives the fallen members of the current lineup. A new account without a current lineup is left alone.
    /// </summary>
    public async ValueTask RepairAtLogin(bool extraLineupOwned)
    {
        if (GetCurLineup() == null) return;

        if (GetCurLineup()!.IsExtraLineup() && !extraLineupOwned)
        {
            SetExtraLineup(ExtraLineupType.LineupNone, []);
            if (GetCurLineup()!.IsExtraLineup()) await SetCurLineup(0);
        }

        foreach (var lineup in Data.Lineups)
        {
            if (lineup.Value.BaseAvatars!.Count > GameConstants.MAX_LINEUP_AVATAR_COUNT)
                lineup.Value.BaseAvatars = lineup.Value.BaseAvatars.GetRange(0, GameConstants.MAX_LINEUP_AVATAR_COUNT);

            foreach (var avatar in lineup.Value.BaseAvatars!)
                if (avatar.BaseAvatarId > 10000)
                {
                    GameData.SpecialAvatarData.TryGetValue(avatar.BaseAvatarId, out var special);
                    if (special != null)
                    {
                        avatar.SpecialAvatarId = special.SpecialAvatarID;
                        avatar.BaseAvatarId = special.AvatarID;
                    }
                    else
                    {
                        GameData.SpecialAvatarData.TryGetValue(avatar.BaseAvatarId * 10 + Player.Data.WorldLevel,
                            out special);
                        if (special != null)
                        {
                            avatar.SpecialAvatarId = special.SpecialAvatarID;
                            avatar.BaseAvatarId = special.AvatarID;
                        }
                    }
                }
        }

        foreach (var avatar in GetCurLineup()!.BaseAvatars!)
        {
            var avatarData = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
            if (avatarData is { CurrentHp: <= 0 })
                // revive
                avatarData.CurrentHp = GameConstants.REVIVE_HP;
        }
    }

    public async ValueTask AddAvatarToCurTeam(int avatarId, bool sendPacket = true)
    {
        await AddAvatar(Data.GetCurLineupIndex(), avatarId, sendPacket);
    }

    // targets the real (non-virtual) lineup even while a story virtual lineup is still active
    public async ValueTask AddAvatarToRealTeam(int avatarId, bool sendPacket = true)
    {
        await AddAvatar(Data.CurLineup, avatarId, sendPacket);
    }

    public async ValueTask AddSpecialAvatarToCurTeam(int specialAvatarId, bool sendPacket = true)
    {
        Data.Lineups.TryGetValue(Data.GetCurLineupIndex(), out var lineup);
        GameData.SpecialAvatarData.TryGetValue(specialAvatarId, out var specialAvatar);
        if (specialAvatar == null) return;
        Player.AvatarManager!.GetTrialAvatar(specialAvatar.SpecialAvatarID)?.CheckLevel(Player.Data.WorldLevel);
        if (lineup == null)
        {
            lineup = new LineupInfo
            {
                Name = "",
                LineupType = 0,
                BaseAvatars =
                [
                    new LineupAvatarInfo
                        { BaseAvatarId = specialAvatar.AvatarID, SpecialAvatarId = specialAvatar.SpecialAvatarID }
                ],
                LineupData = Data,
                AvatarData = Player.AvatarManager!.Data
            };
            Data.Lineups.Add(Data.GetCurLineupIndex(), lineup);
        }
        else
        {
            if (lineup.BaseAvatars!.Count >= GameConstants.MAX_LINEUP_AVATAR_COUNT)
                lineup.BaseAvatars!.RemoveAt(GameConstants.MAX_LINEUP_AVATAR_COUNT - 1);
            lineup.BaseAvatars?.Add(new LineupAvatarInfo
                { BaseAvatarId = specialAvatar.AvatarID, SpecialAvatarId = specialAvatar.SpecialAvatarID });
            Data.Lineups[Data.GetCurLineupIndex()] = lineup;
        }

        if (sendPacket)
        {
            Player.SceneInstance?.SyncLineup();
            await Player.SendPacket(new PacketSyncLineupNotify(lineup));
        }
    }

    public async ValueTask AddTrialAvatarToCurTeam(int specialAvatarId, bool sendPacket = true)
    {
        var lineup = GetCurLineup();
        if (lineup == null) return;
        var trial = Player.AvatarManager!.GetTrialAvatar(specialAvatarId);
        if (trial == null) return;
        var members = lineup.BaseAvatars ??= [];
        if (members.Any(a => a.SpecialAvatarId == trial.SpecialAvatarId)) return; // already joined
        trial.CheckLevel(Player.Data.WorldLevel);

        var slot = new LineupAvatarInfo
            { BaseAvatarId = trial.BaseAvatarId, SpecialAvatarId = trial.SpecialAvatarId };
        // A Trailblazer-path trial replaces the hero in their own slot. Appending it shifted the
        // party and opened the fight on the wrong character.
        var hero = TrailblazerAvatar.IsPath(trial.AvatarId)
            ? members.FindIndex(IsOwnedTrailblazer)
            : -1;
        if (hero >= 0)
        {
            var wasLeader = lineup.LeaderAvatarId == members[hero].BaseAvatarId;
            members[hero] = slot;
            if (wasLeader) lineup.LeaderAvatarId = trial.BaseAvatarId;
        }
        else
        {
            if (members.Count >= GameConstants.MAX_LINEUP_AVATAR_COUNT) members.RemoveAt(members.Count - 1);
            members.Add(slot);
        }

        lineup.IsVirtual = true;
        if (lineup.PlaneId == 0) lineup.PlaneId = Player.Data.PlaneId;
        MarkDirty();

        if (!sendPacket) return;

        await Player.SendPacket(new PacketVirtualLineupTrialAvatarChangeScNotify(lineup.PlaneId,
            [trial.SpecialAvatarId], [], true));
        Player.SceneInstance?.SyncLineup();
        await Player.SendPacket(new PacketSyncLineupNotify(lineup));
    }

    /// <summary>
    ///     A bare CreateTrialPlayer beat names no avatar. The next Trailblazer path gated by this
    ///     mission (or its NextTrackMainMission) is granted when that path has a story-trial row
    ///     (1000000 + path id).
    /// </summary>
    public async ValueTask GrantBareStoryTrial(SubMissionData subMission)
    {
        var specialId = ResolveStoryTrialAvatar(subMission);
        if (specialId != 0)
            await AddTrialAvatarToCurTeam(specialId);
    }

    /// <summary>
    ///     Fixed-level trials resolve SceneEnterStage at world level 0. IsUseWorldLevel trials keep
    ///     the player's world level. Without the raw flag table this stays the player's world level.
    /// </summary>
    public int StageResolveWorldLevel()
    {
        if (!StoryTrialTables.HasSpecialAvatarFlags) return Player.Data.WorldLevel;
        foreach (var slot in GetCurLineup()?.BaseAvatars ?? [])
        {
            if (slot.SpecialAvatarId == 0) continue;
            if (StoryTrialTables.SpecialUsesWorldLevel(slot.SpecialAvatarId)) continue;
            return 0;
        }

        return Player.Data.WorldLevel;
    }

    private int ResolveStoryTrialAvatar(SubMissionData subMission)
    {
        if (subMission.MainMissionId == 0) return 0;
        HashSet<int> chain = [subMission.MainMissionId];
        if (GameData.MainMissionData.TryGetValue(subMission.MainMissionId, out var main) &&
            main.NextTrackMainMission != 0)
            chain.Add(main.NextTrackMainMission);

        foreach (var path in GameData.MultiplePathAvatarConfigData.Values.OrderBy(path => path.AvatarID))
        {
            if (path.BaseAvatarID != TrailblazerAvatar.BaseAvatarId || path.UnlockConditions.Count == 0) continue;
            if (TrailblazerAvatar.NormalizePathId(path.AvatarID, Player.Data.CurrentGender) != path.AvatarID)
                continue;
            if (!UnlockConditionInChain(path.UnlockConditions, chain)) continue;
            var specialId = 1_000_000 + path.AvatarID;
            // Rows are keyed SpecialAvatarID*10+WorldLevel; any world level counts.
            if (Enumerable.Range(0, 10).Any(level => GameData.SpecialAvatarData.ContainsKey(specialId * 10 + level)))
                return specialId;
        }

        return 0;
    }

    private static bool UnlockConditionInChain(List<Condition> conditions, HashSet<int> chain)
    {
        foreach (var condition in conditions)
        {
            if (!int.TryParse(condition.Param, out var id) || id == 0) continue;
            if (condition.Type == ConditionTypeEnum.FinishMainMission && chain.Contains(id)) return true;
            if (condition.Type == ConditionTypeEnum.FinishSubMission &&
                GameData.SubMissionInfoData.TryGetValue(id, out var sub) && chain.Contains(sub.MainMissionId))
                return true;
        }

        return false;
    }

    private static bool IsOwnedTrailblazer(LineupAvatarInfo slot)
    {
        return slot.SpecialAvatarId == 0 &&
               (slot.BaseAvatarId is TrailblazerAvatar.MaleDestructionId or TrailblazerAvatar.FemaleDestructionId ||
                TrailblazerAvatar.IsPath(slot.BaseAvatarId));
    }

    public async ValueTask RemoveTrialAvatarFromCurTeam(int specialAvatarId, bool sendPacket = true)
    {
        var lineup = GetCurLineup();
        if (lineup == null) return;
        if (lineup.BaseAvatars!.RemoveAll(a => a.SpecialAvatarId == specialAvatarId) == 0) return;

        var planeId = lineup.PlaneId != 0 ? lineup.PlaneId : Player.Data.PlaneId;
        var wasVirtual = lineup.IsVirtual;
        var lastTrial = !lineup.BaseAvatars.Any(avatar => avatar.SpecialAvatarId != 0);
        if (lastTrial && wasVirtual)
        {
            lineup.IsVirtual = false;
            lineup.PlaneId = 0;
        }

        // a virtual lineup only exists to host trial avatars; tear it down once the last one leaves,
        // even when the caller suppresses packets, so the saved state never keeps an empty extra lineup
        var tornDown = lineup.IsExtraLineup() && lineup.BaseAvatars!.Count == 0;
        if (tornDown)
        {
            Data.Lineups.Remove(Data.GetCurLineupIndex());
            Data.CurExtraLineup = -1;
        }

        MarkDirty();
        if (!sendPacket) return;

        if (wasVirtual)
            await Player.SendPacket(new PacketVirtualLineupTrialAvatarChangeScNotify(planeId, [],
                [specialAvatarId], false));
        if (lastTrial && wasVirtual && planeId != 0)
            await Player.SendPacket(new PacketVirtualLineupDestroyNotify(planeId));

        if (tornDown)
        {
            // tell the client the extra lineup is gone, then resync the scene against the restored lineup
            await Player.SendPacket(new PacketExtraLineupDestroyNotify((ExtraLineupType)lineup.LineupType));
            if (Player.SceneInstance != null) await Player.SceneInstance.SyncLineup();
            var restored = GetCurLineup();
            if (restored != null) await Player.SendPacket(new PacketSyncLineupNotify(restored));
        }
        else
        {
            if (Player.SceneInstance != null) await Player.SceneInstance.SyncLineup();
            await Player.SendPacket(new PacketSyncLineupNotify(lineup));
        }
    }

    public async ValueTask RemoveAvatar(int lineupIndex, int avatarId, bool sendPacket = true)
    {
        if (lineupIndex < 0) return;
        Data.Lineups.TryGetValue(lineupIndex, out var lineup);
        if (lineup == null) return;
        GameData.SpecialAvatarData.TryGetValue(avatarId * 10 + Player.Data.WorldLevel, out var specialAvatar);
        if (specialAvatar != null)
            lineup.BaseAvatars?.RemoveAll(avatar => avatar.BaseAvatarId == specialAvatar.AvatarID);
        else
            lineup.BaseAvatars?.RemoveAll(avatar => avatar.BaseAvatarId == avatarId);
        Data.Lineups[lineupIndex] = lineup;

        if (sendPacket)
        {
            if (lineupIndex == Data.GetCurLineupIndex())
            {
                RecalculateExtraMpCount();
                Player.SceneInstance?.SyncLineup();
            }
            await Player.SendPacket(new PacketSyncLineupNotify(lineup));
        }
    }

    public async ValueTask RemoveAvatarFromCurTeam(int avatarId, bool sendPacket = true)
    {
        await RemoveAvatar(Data.GetCurLineupIndex(), avatarId, sendPacket);
    }

    public async ValueTask ReplaceLineup(int lineupIndex, List<int> lineupSlotList,
        ExtraLineupType extraLineupType = ExtraLineupType.LineupNone)
    {
        if (extraLineupType != ExtraLineupType.LineupNone)
        {
            Data.CurExtraLineup = (int)extraLineupType + 10;
            if (!Data.Lineups.ContainsKey(Data.CurExtraLineup)) SetExtraLineup(extraLineupType, []);
        }

        LineupInfo lineup;
        if (Data.CurExtraLineup != -1)
            lineup = Data.Lineups[Data.CurExtraLineup]; // Extra lineup
        else if (lineupIndex < 0 || !Data.Lineups.TryGetValue(lineupIndex, out var dataLineup))
            return;
        else
            lineup = dataLineup;
        lineup.BaseAvatars = [];
        var index = lineup.LineupType == 0 ? lineupIndex : Data.GetCurLineupIndex();
        foreach (var avatar in lineupSlotList) await AddAvatar(index, avatar, false);
        lineup.LeaderAvatarId = lineup.BaseAvatars.FirstOrDefault()?.BaseAvatarId ?? 0;

        if (index == Data.GetCurLineupIndex())
        {
            RecalculateExtraMpCount();
            Player.SceneInstance?.SyncLineup();
        }
        await Player.SendPacket(new PacketSyncLineupNotify(lineup));
    }

    /// <summary>Puts the support into the current normal lineup: replaces an existing support slot, else fills the
    ///     first free slot, else the last one. Story, raid and other extra lineups take no support.</summary>
    public bool SetAssistSlot(int ownerUid, int ownerBaseAvatarId)
    {
        var lineup = GetCurLineup();
        if (lineup?.BaseAvatars == null || lineup.LineupType != 0 || lineup.IsVirtual) return false;

        var slot = new LineupAvatarInfo { BaseAvatarId = ownerBaseAvatarId, AssistUid = ownerUid };
        var existing = lineup.BaseAvatars.FindIndex(avatar => avatar.AssistUid != 0);
        if (existing >= 0)
            lineup.BaseAvatars[existing] = slot;
        else if (lineup.BaseAvatars.Count < GameConstants.MAX_LINEUP_AVATAR_COUNT)
            lineup.BaseAvatars.Add(slot);
        else
            lineup.BaseAvatars[^1] = slot;

        if (lineup.BaseAvatars.All(avatar => avatar.BaseAvatarId != lineup.LeaderAvatarId || avatar.AssistUid != 0))
            lineup.LeaderAvatarId = lineup.BaseAvatars.FirstOrDefault(avatar => avatar.AssistUid == 0)?.BaseAvatarId ??
                                    lineup.LeaderAvatarId;
        MarkDirty();
        return true;
    }

    public void ClearAssistSlots()
    {
        foreach (var lineup in Data.Lineups.Values)
            if (lineup.BaseAvatars?.RemoveAll(avatar => avatar.AssistUid != 0) > 0)
                MarkDirty();
    }

    public async ValueTask ReplaceLineup(ReplaceLineupCsReq req)
    {
        var hadAssist = GetCurLineup()?.BaseAvatars?.Any(avatar => avatar.AssistUid != 0) == true;
        if (req.ExtraLineupType != ExtraLineupType.LineupNone)
        {
            Data.CurExtraLineup = (int)req.ExtraLineupType + 10;
            if (!Data.Lineups.ContainsKey(Data.CurExtraLineup)) SetExtraLineup(req.ExtraLineupType, []);
        }

        LineupInfo lineup;
        if (Data.CurExtraLineup != -1)
            lineup = Data.Lineups[Data.CurExtraLineup]; // Extra lineup
        else if (!Data.Lineups.ContainsKey((int)req.Index))
            return;
        else
            lineup = Data.Lineups[(int)req.Index];
        lineup.BaseAvatars = [];
        var index = lineup.LineupType == 0 ? (int)req.Index : Data.GetCurLineupIndex();
        foreach (var avatar in req.LineupSlotList.OrderBy(slot => slot.Slot))
        {
            if (avatar.Id == 0) continue;
            if (avatar.AssistUid == 0 || avatar.AssistUid == Player.Uid)
            {
                await AddAvatar(index, (int)avatar.Id, false);
                continue;
            }

            // A support slot keeps the owner's avatar instead of swapping in (or dropping for) the player's own copy.
            var ownerAvatars = DatabaseHelper.Instance?.GetInstance<AvatarData>((int)avatar.AssistUid);
            var support = ownerAvatars?.FindByAnyId((int)avatar.Id);
            if (support == null || !ownerAvatars!.AssistAvatars.Contains(support.BaseAvatarId) ||
                lineup.LineupType != 0 || lineup.BaseAvatars.Any(slot => slot.AssistUid != 0))
                continue;
            lineup.BaseAvatars.Add(new LineupAvatarInfo
                { BaseAvatarId = support.BaseAvatarId, AssistUid = (int)avatar.AssistUid });
        }

        lineup.LeaderAvatarId = lineup.BaseAvatars.FirstOrDefault(slot => slot.AssistUid == 0)?.BaseAvatarId ??
                                lineup.BaseAvatars.FirstOrDefault()?.BaseAvatarId ?? 0;

        // The client rebuilds its support member from CurAssistChangedNotify, which must precede the lineup sync.
        var assistSlot = lineup.BaseAvatars.FirstOrDefault(slot => slot.AssistUid != 0);
        if (assistSlot != null)
            await Player.SendPacket(new PacketCurAssistChangedNotify(
                FriendManager.BuildAssistInfo(assistSlot.AssistUid, assistSlot.BaseAvatarId) ??
                new PlayerAssistInfo()));
        else if (hadAssist)
            await Player.SendPacket(new PacketCurAssistChangedNotify(new PlayerAssistInfo()));

        if (index == Data.GetCurLineupIndex())
        {
            RecalculateExtraMpCount();
            Player.SceneInstance?.SyncLineup();
        }
        await Player.SendPacket(new PacketSyncLineupNotify(lineup));
    }

    public async ValueTask DestroyExtraLineup(ExtraLineupType type)
    {
        var index = (int)type + 10;
        Data.Lineups.Remove(index);
        await Player.SendPacket(new PacketExtraLineupDestroyNotify(type));
    }

    public async ValueTask CostMp(int count, uint castEntityId = 1)
    {
        var curLineup = GetCurLineup()!;
        curLineup.Mp -= count;
        curLineup.Mp = Math.Min(Math.Max(0, curLineup.Mp), GetMaxMp());

        await Player.SendPacket(new PacketSceneCastSkillMpUpdateScNotify(castEntityId, curLineup.Mp));
    }

    public async ValueTask GainMp(int count, bool sendPacket = true,
        SyncLineupReason reason = SyncLineupReason.SyncReasonNone)
    {
        var curLineup = GetCurLineup()!;
        curLineup.Mp += count;
        curLineup.Mp = Math.Min(Math.Max(0, curLineup.Mp), GetMaxMp());
        if (sendPacket)
            await Player.SendPacket(
                new PacketSyncLineupNotify(GetCurLineup()!, reason));
    }

    #endregion
}

public record AvatarLineupData(BaseAvatarInfo AvatarInfo, AvatarType AvatarType, int? BattleAvatarId = null,
    int AssistUid = 0);
