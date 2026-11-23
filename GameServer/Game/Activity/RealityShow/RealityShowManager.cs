using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.RealityShow;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Lineup;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.GameServer.Server.Packet.Send.RealityShow;
using March7thHoney.Proto;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;

namespace March7thHoney.GameServer.Game.Activity.RealityShow;

/// <summary>A warm-up or final stage this player is fighting.</summary>
public sealed class RealityShowBattleContext(
    RealityShowChallengeConfig challenge,
    bool isFirstStage,
    List<AvatarLineupData> lineupOverride)
{
    public RealityShowChallengeConfig Challenge { get; } = challenge;
    public bool IsFirstStage { get; } = isFirstStage;
    public List<AvatarLineupData> LineupOverride { get; } = lineupOverride;
}

/// <summary>Quest argument raised after a stage settles; the grades are the challenge's current ones.</summary>
public readonly record struct RealityShowStageSettled(int ChallengeId, bool IsFirstStage, int BuffLevel, int ResultLevel);

/// <summary>
///     Interastral Peace Gala: Road to Fame (星际和平盛典：成名之路, client module ActivityRealityShow, panel 50143).
///     Each challenge is two battles. The warm-up (StageID1) counts the special shields broken; the count buys up to
///     three final-stage buffs (BuffUpNumList thresholds). The final stage (StageID2) is scored by the boss HP bars
///     broken before the turn limit, ties going to less action value consumed. Both battles run on the client, which
///     reports its counters in BattleStatistics.MHPDKEDHELL.MFOHGCNOMLB (RealityShow sub-mode statistics); the server
///     keeps the records and answers command group 10785-10799. Field meanings are recovered from the client code
///     and listed in docs/development/reality-show.md.
/// </summary>
public class RealityShowManager(PlayerInstance player) : BasePlayerComponent(player)
{
    public const int LineupSlotCount = 4;

    public PlayerRealityShowData Data { get; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<PlayerRealityShowData>(player.Uid);

    private static RealityShowConfig Config => GameData.RealityShowConfig;

    private void MarkDirty() => DatabaseHelper.MarkDirty(Player.Uid);

    #region Protocol

    /// <summary>The challenge as the client's MJCMGOEMKCC.DBADKFDBOHF reads it (LFIKNHJBDBJ).</summary>
    public LFIKNHJBDBJ ToChallengeProto(int challengeId)
    {
        var record = Data.Challenges.GetValueOrDefault(challengeId);
        var proto = new LFIKNHJBDBJ { ChallengeId = (uint)challengeId };
        if (record == null) return proto;

        proto.LMINNGCDDOK = record.IsFirstStageFinished;
        proto.MNIBAEDPCHA = (uint)record.ShieldBreakCount;
        proto.HAGDKKBAAKK = (uint)record.BuffLevel;
        proto.CNEJKLABHDG = (uint)record.MaxHpBarBreakCount;
        proto.LIHEPONEGOB = record.ConsumedActionValue;
        proto.KHBJOAHCCCE.AddRange(ToLineupProto(record.Lineup));
        return proto;
    }

    /// <summary>GetData reply: every configured challenge, in id order.</summary>
    public List<LFIKNHJBDBJ> ToChallengeListProto()
    {
        return Config.Challenges.OrderBy(x => x.Id).Select(x => ToChallengeProto(x.Id)).ToList();
    }

    public static IEnumerable<KPNHGDGLICN> ToLineupProto(IEnumerable<RealityShowLineupAvatar> lineup)
    {
        return lineup.Select(x => new KPNHGDGLICN { AvatarType = (AvatarType)x.AvatarType, Id = (uint)x.Id });
    }

    /// <summary>Friend ranking entries (CFIIJJEGKHC) of the friends that have a final-stage record.</summary>
    public List<CFIIJJEGKHC> ToFriendRankProto(int challengeId)
    {
        var list = new List<CFIIJJEGKHC>();
        foreach (var friend in Player.FriendManager?.GetFriendPlayerData() ?? [])
        {
            var data = DatabaseHelper.Instance?.GetInstance<PlayerRealityShowData>(friend.Uid);
            var record = data?.Challenges.GetValueOrDefault(challengeId);
            if (record == null || record.MaxHpBarBreakCount <= 0) continue;
            var entry = new CFIIJJEGKHC
            {
                Uid = (uint)friend.Uid,
                CNEJKLABHDG = (uint)record.MaxHpBarBreakCount,
                LIHEPONEGOB = record.ConsumedActionValue
            };
            entry.OEOPMBHFBBP.AddRange(record.BestAvatarIds.Select(x => (uint)x));
            list.Add(entry);
        }

        return list;
    }

    #endregion

    #region Challenge

    private RealityShowChallengeRecord GetOrCreateRecord(int challengeId)
    {
        if (!Data.Challenges.TryGetValue(challengeId, out var record))
            Data.Challenges[challengeId] = record = new RealityShowChallengeRecord { ChallengeId = challengeId };
        return record;
    }

    /// <summary>ResetFirstStage (10794): drop the warm-up result so the warm-up can be fought again.</summary>
    public Retcode ResetFirstStage(int challengeId)
    {
        if (Config.GetChallenge(challengeId) == null) return Retcode.RetChallengeNotExist;
        if (Player.BattleInstance?.RealityShowContext?.Challenge.Id == challengeId) return Retcode.RetInBattleNow;
        if (!Data.Challenges.TryGetValue(challengeId, out var record)) return Retcode.RetSucc;

        record.IsFirstStageFinished = false;
        record.ShieldBreakCount = 0;
        record.BuffLevel = 0;
        MarkDirty();
        return Retcode.RetSucc;
    }

    /// <summary>
    ///     StartChallenge (10798): fight the warm-up (<paramref name="isFirstStage" />) or the final stage with up to
    ///     four avatars. Trial avatars carry their SpecialAvatar id and must be offered by the challenge; own avatars
    ///     (formal, or "upgrade available" when they replace a trial avatar) carry their base id.
    /// </summary>
    public (Retcode, BattleInstance?) StartChallenge(int challengeId, bool isFirstStage,
        IReadOnlyList<(AvatarType Type, int Id)> avatarList)
    {
        if (Player.BattleInstance != null) return (Retcode.RetInBattleNow, null);
        var challenge = Config.GetChallenge(challengeId);
        if (challenge == null) return (Retcode.RetChallengeNotExist, null);
        if (!MissionManager.IsActivityModuleScheduled(challenge.ModuleId)) return (Retcode.RetChallengeNotUnlock, null);
        var record = Data.Challenges.GetValueOrDefault(challengeId);
        if (!isFirstStage && record?.IsFirstStageFinished != true) return (Retcode.RetChallengeNotFinish, null);
        if (!GameData.StageConfigData.TryGetValue(isFirstStage ? challenge.StageId1 : challenge.StageId2, out var stage))
            return (Retcode.RetStageConfigNotExist, null);
        if (avatarList.Count == 0) return (Retcode.RetChallengeLineupEmpty, null);
        if (avatarList.Count > LineupSlotCount) return (Retcode.RetReqParaInvalid, null);

        var avatarManager = Player.AvatarManager!;
        // Rebuild the trial avatars so their form and state come fresh from the excel every run.
        avatarManager.Data.TrialAvatars.RemoveAll(x =>
            avatarList.Any(a => a.Type == AvatarType.AvatarTrialType && a.Id == x.SpecialAvatarId));

        var lineupOverride = new List<AvatarLineupData>();
        var baseAvatars = new List<LineupAvatarInfo>();
        foreach (var (type, id) in avatarList)
        {
            BaseAvatarInfo? avatar;
            switch (type)
            {
                case AvatarType.AvatarTrialType:
                    if (!challenge.SpecialAvatarList.Contains(id)) return (Retcode.RetChallengeLineupAvatarTypeInvalid, null);
                    avatar = avatarManager.GetTrialAvatarByWorldLevel(id, Player.Data.WorldLevel);
                    break;
                case AvatarType.AvatarFormalType or AvatarType.AvatarUpgradeAvailableType:
                    avatar = avatarManager.GetFormalAvatar(id);
                    break;
                default:
                    return (Retcode.RetChallengeLineupAvatarTypeInvalid, null);
            }

            if (avatar == null) return (Retcode.RetAvatarNotExist, null);
            if (lineupOverride.Any(x => x.AvatarInfo.BaseAvatarId == avatar.BaseAvatarId))
                return (Retcode.RetLineupAvatarAlreadyIn, null);

            lineupOverride.Add(new AvatarLineupData(avatar, type, avatar is FormalAvatarInfo ? id : null));
            baseAvatars.Add(avatar is SpecialAvatarInfo special
                ? new LineupAvatarInfo { BaseAvatarId = special.BaseAvatarId, SpecialAvatarId = special.SpecialAvatarId }
                : new LineupAvatarInfo { BaseAvatarId = avatar.BaseAvatarId });
        }

        var lineup = new LineupInfo
        {
            LineupType = (int)ExtraLineupType.LineupActivity,
            LeaderAvatarId = baseAvatars[0].BaseAvatarId,
            BaseAvatars = baseAvatars,
            AvatarData = avatarManager.Data
        };
        var battle = new BattleInstance(Player, lineup, [stage])
        {
            WorldLevel = Player.Data.WorldLevel,
            LeaderIndexOverride = 0,
            RealityShowContext = new RealityShowBattleContext(challenge, isFirstStage, lineupOverride)
        };

        // The warm-up's own buff is bound by its stage (_BindingMazeBuff); the final stage carries the tiers earned.
        if (!isFirstStage)
            foreach (var buffId in challenge.BuffIdList.Take(record!.BuffLevel))
                battle.Buffs.Add(new MazeBuff(buffId, 1, -1) { WaveFlag = -1 });

        record = GetOrCreateRecord(challengeId);
        record.Lineup = avatarList.Select(x => new RealityShowLineupAvatar { AvatarType = (int)x.Type, Id = x.Id }).ToList();
        MarkDirty();

        battle.OnBattleEnd += OnBattleEnd;
        Player.QuestManager!.OnBattleStart(battle);
        Player.BattleInstance = battle;
        return (Retcode.RetSucc, battle);
    }

    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        var context = battle.RealityShowContext;
        if (context == null || req.EndStatus != BattleEndStatus.BattleEndWin) return;

        var challenge = context.Challenge;
        var record = GetOrCreateRecord(challenge.Id);
        var counters = req.Stt?.MHPDKEDHELL?.MFOHGCNOMLB;
        NOEGOMIJDAH notify;
        if (context.IsFirstStage)
        {
            var shieldBreakCount = (int)(counters?.MNIBAEDPCHA ?? 0);
            record.IsFirstStageFinished = true;
            record.ShieldBreakCount = shieldBreakCount;
            record.BuffLevel = challenge.GetBuffLevel(shieldBreakCount);
            notify = new NOEGOMIJDAH
            {
                ChallengeId = (uint)challenge.Id,
                BCLOOPGAIKJ = new CGCIGGBIGAL
                {
                    HAGDKKBAAKK = (uint)record.BuffLevel,
                    MNIBAEDPCHA = (uint)shieldBreakCount
                }
            };
        }
        else
        {
            var hpBarBreakCount = (int)(counters?.KMBPEBAFDLF ?? 0);
            var consumedActionValue = req.Stt?.TotalDelayCumulate ?? 0;
            if (IsNewRecord(record, hpBarBreakCount, consumedActionValue))
            {
                record.MaxHpBarBreakCount = hpBarBreakCount;
                record.ConsumedActionValue = consumedActionValue;
                record.BestAvatarIds = context.LineupOverride.Select(x => x.AvatarInfo.BaseAvatarId).ToList();
            }

            notify = new NOEGOMIJDAH
            {
                ChallengeId = (uint)challenge.Id,
                OEFFDKDCIMC = new HAIEHHIHIGC
                {
                    NKIIHODEFBP = (uint)hpBarBreakCount,
                    FMBFAPOCIDF = consumedActionValue
                }
            };
        }

        MarkDirty();
        await Player.SendPacket(new PacketRealityShowSettleScNotify(notify));

        var settled = new RealityShowStageSettled(challenge.Id, context.IsFirstStage, record.BuffLevel,
            record.MaxHpBarBreakCount > 0 ? Config.GetResultLevel(record.MaxHpBarBreakCount) : 0);
        await Player.MissionManager!.HandleFinishType(context.IsFirstStage
            ? MissionFinishTypeEnum.RealityShowBuffNum
            : MissionFinishTypeEnum.RealityShowResult, settled);
    }

    /// <summary>
    ///     The client's rule (MJCMGOEMKCC.HOAHFAPHDND): a run with no HP bar never counts; more HP bars win; equal
    ///     HP bars win with less action value consumed.
    /// </summary>
    public static bool IsNewRecord(RealityShowChallengeRecord record, int hpBarBreakCount, double consumedActionValue)
    {
        if (hpBarBreakCount <= 0) return false;
        if (hpBarBreakCount != record.MaxHpBarBreakCount) return hpBarBreakCount > record.MaxHpBarBreakCount;
        return Math.Round(consumedActionValue) < Math.Round(record.ConsumedActionValue);
    }

    #endregion
}
