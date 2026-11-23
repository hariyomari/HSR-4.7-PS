using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Database;
using March7thHoney.Database.DiceyDungeon;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Activity.DiceyDungeon;

/// <summary>
///     Containment Starts With a Roll (骰子收容, client module DiceyDungeon, activity 50140 / module 5014001).
///     The dice battles run on the client; the server owns the run: which level and node is being played, the
///     avatar, HP, equipped skills and miracles, the cleared nodes, and the reward choice offered after a win.
///     The client's data model (FLIFOAOEFPP / MJHOIBEANAJ in the 4.7.51 dump) moves to PendingChallengeNode,
///     PendingReward or LevelSettling purely from what these replies carry, so every reply that changes the run
///     returns the whole run. Protocol field meanings are recovered from the client code and listed in
///     docs/development/dicey-dungeon.md.
/// </summary>
public class DiceyDungeonManager(PlayerInstance player) : BasePlayerComponent(player)
{
    private static readonly Random Rng = Random.Shared;

    public PlayerDiceyDungeonData Data { get; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<PlayerDiceyDungeonData>(player.Uid);

    private static DiceyDungeonConfig Config => GameData.DiceyDungeonConfig;

    private DiceyDungeonRunData Run => Data.Run;
    private bool HasRun => Run.LevelId != 0;

    private void MarkDirty() => DatabaseHelper.MarkDirty(Player.Uid);

    #region Protocol

    /// <summary>GetData reply body: the run in progress (if any) and a record for every configured level.</summary>
    public LCIKABPABHK ToDataProto()
    {
        var proto = new LCIKABPABHK { NBNKLEBNPFA = ToRunProto() };
        foreach (var level in Config.Levels.OrderBy(x => x.Id))
        {
            var record = Data.Levels.GetValueOrDefault(level.Id);
            proto.IADLNIOADDN.Add(new OGDOODOGIOF
            {
                IJAPHCOHMIN = (uint)level.Id,
                HCOFMFKFMDK = record?.IsFinished ?? false,
                BDCOFIOCJFO = (uint)(record?.Star ?? 0),
                GKALFLHFPKC = (uint)(record?.FarthestNodeId ?? 0)
            });
        }

        return proto;
    }

    /// <summary>The run as the client's MJHOIBEANAJ.ALJIOCDBMKO reads it; null clears the client's run.</summary>
    public HBBPNKMJFBF? ToRunProto()
    {
        if (!HasRun) return null;
        var proto = new HBBPNKMJFBF
        {
            IJAPHCOHMIN = (uint)Run.LevelId,
            ILHPFNCIOJN = (uint)Run.AvatarId,
            JCLIFKACIJD = Run.SerialNumber,
            FHHOPLLDPAP = (uint)Run.CurrentNodeId,
            OMIIAOCIIPA = (uint)Math.Max(Run.CurrentHp, 0),
            DPLODKBIJDI = ToRewardProto(Run.PendingSkill),
            CACADJOIHHI = ToRewardProto(Run.PendingMiracle)
        };
        proto.JHLGHLBDMLH.AddRange(Run.SkillIds.Select(x => (uint)x));
        proto.KIIIDKADAIN.AddRange(Run.MiracleIds.Select(x => (uint)x));
        proto.BAFLDOEHBJK.AddRange(Run.FinishedNodeIds.Select(x => (uint)x));
        return proto;
    }

    private static MHLHDALAFMJ? ToRewardProto(DiceyDungeonPendingReward reward)
    {
        if (reward.RewardIds.Count == 0) return null;
        var proto = new MHLHDALAFMJ
        {
            MNBMPEIABNJ = reward.CanRecoverHp,
            LBDOBLGKANH = (uint)Math.Max(reward.LeftRerollTime, 0)
        };
        proto.GGBGEJIIKFN.AddRange(reward.RewardIds.Select(x => (uint)x));
        return proto;
    }

    #endregion

    #region Node entry

    /// <summary>
    ///     Enter a node: the first node of a level starts a new run (a run of another level is dropped), the node
    ///     after the last won one moves the run on, and the current unfinished node can be re-entered after a loss.
    /// </summary>
    public Retcode EnterNode(int levelId, int nodeId, int avatarId, IReadOnlyList<int> skillIds,
        IReadOnlyList<int> miracleIds)
    {
        var level = Config.GetLevel(levelId);
        var node = Config.GetNode(nodeId);
        if (level == null || node == null || node.LevelId != levelId) return Retcode.RetFail;
        if (!IsLevelUnlocked(level)) return Retcode.RetFail;

        if (node.PreNodeId == 0)
        {
            var loadout = BuildInitialLoadout(level, avatarId, skillIds, miracleIds);
            if (loadout == null) return Retcode.RetFail;
            var avatar = Config.GetAvatar(loadout.Value.AvatarId)!;
            Data.Run = new DiceyDungeonRunData
            {
                LevelId = levelId,
                AvatarId = avatar.Id,
                CurrentNodeId = nodeId,
                CurrentHp = avatar.InitialHp,
                SkillIds = loadout.Value.Skills,
                MiracleIds = loadout.Value.Miracles
            };
        }
        else if (!HasRun || Run.LevelId != levelId || HasPendingReward)
        {
            return Retcode.RetFail;
        }
        else if (Run.CurrentNodeId == nodeId)
        {
            if (Run.FinishedNodeIds.Contains(nodeId)) return Retcode.RetFail;
        }
        else if (node.PreNodeId == Run.CurrentNodeId && Run.FinishedNodeIds.Contains(Run.CurrentNodeId))
        {
            Run.CurrentNodeId = nodeId;
        }
        else
        {
            return Retcode.RetFail;
        }

        Run.SerialNumber = ++Data.NextSerial;
        MarkDirty();
        return Retcode.RetSucc;
    }

    private bool IsLevelUnlocked(DiceyDungeonLevelConfig level)
    {
        return level.PreLevelId == 0 || Data.Levels.GetValueOrDefault(level.PreLevelId)?.IsFinished == true;
    }

    /// <summary>
    ///     Story levels play their own avatar with its story skills. The challenge level lets the player pick one of
    ///     the listed avatars and a loadout out of that avatar's challenge skills and miracles.
    /// </summary>
    private (int AvatarId, List<int> Skills, List<int> Miracles)? BuildInitialLoadout(DiceyDungeonLevelConfig level,
        int avatarId, IReadOnlyList<int> skillIds, IReadOnlyList<int> miracleIds)
    {
        if (!level.IsChallenge)
        {
            var storyAvatar = Config.GetAvatar(level.AvatarId);
            if (storyAvatar == null || (avatarId != 0 && avatarId != storyAvatar.Id)) return null;
            return (storyAvatar.Id, [..storyAvatar.StoryInitialSkillIds], []);
        }

        if (!level.ChallengeAvatarIds.Contains(avatarId)) return null;
        var avatar = Config.GetAvatar(avatarId);
        if (avatar == null) return null;

        var allowedSkills = avatar.ChallengeInitialSkillIds.Concat(avatar.ChallengeSkillIds).ToHashSet();
        var skills = skillIds.Distinct().ToList();
        var miracles = miracleIds.Distinct().ToList();
        if (skills.Count == 0) skills = [..avatar.ChallengeInitialSkillIds];
        if (skills.Count > Config.MaxSkillNum || !skills.All(allowedSkills.Contains)) return null;
        if (miracles.Count > Config.MaxMiracleNumChallenge || !miracles.All(avatar.ChallengeMiracleIds.Contains))
            return null;
        return (avatar.Id, skills, miracles);
    }

    #endregion

    #region Node finish

    public sealed record FinishResult(Retcode Retcode, bool LevelFinished = false, int Star = 0, int FarthestNodeId = 0);

    /// <summary>
    ///     Settle the current node. A story win clears the node and offers the reward choice, or finishes the level
    ///     after the last node; a story loss keeps the run on the same node so it can be retried. The challenge
    ///     level has one node and always settles, its stars counted from the damage dealt to the boss.
    /// </summary>
    public async ValueTask<FinishResult> FinishNode(int levelId, int nodeId, bool isWin, int leftHp, int enemyLeftHp)
    {
        if (!HasRun || Run.LevelId != levelId || Run.CurrentNodeId != nodeId) return new FinishResult(Retcode.RetFail);
        if (Run.FinishedNodeIds.Contains(nodeId) || HasPendingReward) return new FinishResult(Retcode.RetFail);
        var level = Config.GetLevel(levelId);
        var node = Config.GetNode(nodeId);
        var avatar = Config.GetAvatar(Run.AvatarId);
        if (level == null || node == null || avatar == null) return new FinishResult(Retcode.RetFail);

        var record = Data.Levels.GetValueOrDefault(levelId) ?? new DiceyDungeonLevelRecord { LevelId = levelId };
        Data.Levels[levelId] = record;

        if (level.IsChallenge)
        {
            var star = ChallengeStar(node, isWin, enemyLeftHp);
            record.Star = Math.Max(record.Star, star);
            record.IsFinished |= star > 0;
            if (isWin) WinNode(record, nodeId);
            Data.Run = new DiceyDungeonRunData();
            MarkDirty();
            if (isWin) await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.DiceyDungeonNodeFinish, nodeId);
            if (star > 0) await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.DiceyDungeonLevelFinish, levelId);
            return new FinishResult(Retcode.RetSucc, true, record.Star, record.FarthestNodeId);
        }

        if (!isWin)
        {
            MarkDirty();
            return new FinishResult(Retcode.RetSucc, false, record.Star, record.FarthestNodeId);
        }

        WinNode(record, nodeId);
        Run.CurrentHp = Math.Clamp(leftHp, 0, avatar.InitialHp);
        var levelFinished = Config.GetNextNode(nodeId) == null;
        if (levelFinished)
        {
            record.IsFinished = true;
            Data.Run = new DiceyDungeonRunData();
        }
        else
        {
            OfferRewards(node);
        }

        MarkDirty();
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.DiceyDungeonNodeFinish, nodeId);
        if (levelFinished)
            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.DiceyDungeonLevelFinish, levelId);
        return new FinishResult(Retcode.RetSucc, levelFinished, record.Star, record.FarthestNodeId);
    }

    private void WinNode(DiceyDungeonLevelRecord record, int nodeId)
    {
        if (!Run.FinishedNodeIds.Contains(nodeId)) Run.FinishedNodeIds.Add(nodeId);
        if (!Data.ClearedNodeIds.Contains(nodeId)) Data.ClearedNodeIds.Add(nodeId);
        if (NodeOrder(nodeId) > NodeOrder(record.FarthestNodeId)) record.FarthestNodeId = nodeId;
    }

    private static int NodeOrder(int nodeId)
    {
        var order = 0;
        for (var node = Config.GetNode(nodeId); node != null; node = Config.GetNode(node.PreNodeId)) order++;
        return order;
    }

    /// <summary>Challenge_Star_Percent lists the share of the boss HP dealt for 3, 2 and 1 stars.</summary>
    private static int ChallengeStar(DiceyDungeonNodeConfig node, bool isWin, int enemyLeftHp)
    {
        var maxHp = Config.GetMonster(node.MonsterId)?.Hp ?? 0;
        var dealt = isWin || maxHp <= 0 ? 100 : (maxHp - Math.Clamp(enemyLeftHp, 0, maxHp)) * 100 / maxHp;
        var thresholds = Config.ChallengeStarPercent;
        for (var i = 0; i < thresholds.Count; i++)
            if (dealt >= thresholds[i]) return thresholds.Count - i;
        return 0;
    }

    #endregion

    #region Rewards

    public bool HasPendingReward => HasRun && (Run.PendingSkill.RewardIds.Count > 0 || Run.PendingMiracle.RewardIds.Count > 0);

    /// <summary>A skill choice after every story win, and a miracle choice as well after an elite.</summary>
    private void OfferRewards(DiceyDungeonNodeConfig node)
    {
        var canRecover = Config.RecoverUnlockNodeId == 0 || Data.ClearedNodeIds.Contains(Config.RecoverUnlockNodeId);
        var rerolls = Config.RerollUnlockNodeId == 0 || Data.ClearedNodeIds.Contains(Config.RerollUnlockNodeId)
            ? Config.MaxRerollTime
            : 0;

        var skills = node.FixedSkillIds.Count > 0 ? [..node.FixedSkillIds] : DrawSkills([]);
        Run.PendingSkill = new DiceyDungeonPendingReward
            { RewardIds = skills, CanRecoverHp = canRecover, LeftRerollTime = rerolls };

        var miracles = node.FixedMiracleIds.Count > 0 ? [..node.FixedMiracleIds] : node.IsElite ? DrawMiracles([]) : [];
        Run.PendingMiracle = new DiceyDungeonPendingReward
            { RewardIds = miracles, CanRecoverHp = canRecover, LeftRerollTime = miracles.Count > 0 ? rerolls : 0 };
    }

    private List<int> DrawSkills(IReadOnlyCollection<int> exclude)
    {
        return Draw(Config.SkillPool, Run.SkillIds.Concat(exclude).ToHashSet());
    }

    private List<int> DrawMiracles(IReadOnlyCollection<int> exclude)
    {
        return Draw(Config.MiraclePool, Run.MiracleIds.Concat(exclude).ToHashSet());
    }

    /// <summary>Weighted draw without repeats from the avatar's pool, leaving out what the run already owns.</summary>
    private List<int> Draw(List<DiceyDungeonPoolEntry> pool, HashSet<int> exclude)
    {
        var candidates = pool.Where(x => x.AvatarId == Run.AvatarId && x.Weight > 0 && !exclude.Contains(x.Id))
            .GroupBy(x => x.Id).Select(g => g.First()).ToList();
        var result = new List<int>();
        while (result.Count < Config.RewardCandidateCount && candidates.Count > 0)
        {
            var roll = Rng.Next(candidates.Sum(x => x.Weight));
            var index = 0;
            while (roll >= candidates[index].Weight) roll -= candidates[index++].Weight;
            result.Add(candidates[index].Id);
            candidates.RemoveAt(index);
        }

        return result;
    }

    /// <summary>
    ///     Answer the pending choice (the skill one first, as the client's MJHOIBEANAJ.IEGCEOAFODM shows it): take a
    ///     skill, replacing an equipped one once the slots are full, take a miracle, or give the reward up and
    ///     recover HP when that is allowed.
    /// </summary>
    public Retcode SelectReward(KPNDLNCAECC req)
    {
        if (!HasPendingReward) return Retcode.RetFail;
        var avatar = Config.GetAvatar(Run.AvatarId);
        if (avatar == null) return Retcode.RetFail;

        switch (req.IJGHNHPEHDPCase)
        {
            case KPNDLNCAECC.IJGHNHPEHDPOneofCase.ALPKKNKAFBG:
            {
                if (Run.PendingSkill.RewardIds.Count == 0) return Retcode.RetFail;
                // KMAEFBHJFDM: GHMHMKINNEJ is the skill taken, KFGFGMFAMDA the equipped one it replaces (0 for none).
                var chosen = (int)req.ALPKKNKAFBG.GHMHMKINNEJ;
                var replaced = (int)req.ALPKKNKAFBG.KFGFGMFAMDA;
                if (!Run.PendingSkill.RewardIds.Contains(chosen)) return Retcode.RetFail;
                if (replaced != 0)
                {
                    var slot = Run.SkillIds.IndexOf(replaced);
                    if (slot < 0) return Retcode.RetFail;
                    Run.SkillIds[slot] = chosen;
                }
                else
                {
                    if (Run.SkillIds.Count >= Config.MaxSkillNum) return Retcode.RetFail;
                    Run.SkillIds.Add(chosen);
                }

                Run.PendingSkill = new DiceyDungeonPendingReward();
                break;
            }
            case KPNDLNCAECC.IJGHNHPEHDPOneofCase.ANBKDJKHCBI:
            {
                var chosen = (int)req.ANBKDJKHCBI.LJCEOGPOGBE;
                if (Run.PendingSkill.RewardIds.Count > 0 || !Run.PendingMiracle.RewardIds.Contains(chosen))
                    return Retcode.RetFail;
                if (!Run.MiracleIds.Contains(chosen)) Run.MiracleIds.Add(chosen);
                Run.PendingMiracle = new DiceyDungeonPendingReward();
                break;
            }
            case KPNDLNCAECC.IJGHNHPEHDPOneofCase.POJDOCAKCJM:
            case KPNDLNCAECC.IJGHNHPEHDPOneofCase.NBNMHLIFCMF:
            {
                var current = Run.PendingSkill.RewardIds.Count > 0 ? Run.PendingSkill : Run.PendingMiracle;
                if (current.CanRecoverHp)
                    Run.CurrentHp = Math.Min(Run.CurrentHp + Config.GiveUpRecoverHp, avatar.InitialHp);
                current.RewardIds.Clear();
                break;
            }
            default:
                return Retcode.RetFail;
        }

        MarkDirty();
        return Retcode.RetSucc;
    }

    /// <summary>Draw a new set for one reward type while rerolls are left. Returns the type rerolled.</summary>
    public (Retcode Retcode, bool IsMiracle) RerollReward(IMPHFFIJDKP req)
    {
        if (!HasRun) return (Retcode.RetFail, false);
        var isMiracle = req.GPJHBOKGHGOCase == IMPHFFIJDKP.GPJHBOKGHGOOneofCase.ANBKDJKHCBI;
        if (!isMiracle && req.GPJHBOKGHGOCase != IMPHFFIJDKP.GPJHBOKGHGOOneofCase.ALPKKNKAFBG)
            return (Retcode.RetFail, false);

        var reward = isMiracle ? Run.PendingMiracle : Run.PendingSkill;
        if (reward.RewardIds.Count == 0 || reward.LeftRerollTime <= 0) return (Retcode.RetFail, isMiracle);

        var redrawn = isMiracle ? DrawMiracles(reward.RewardIds) : DrawSkills(reward.RewardIds);
        // A pool too small for a fresh set keeps the old candidates rather than offering nothing.
        if (redrawn.Count > 0) reward.RewardIds = redrawn;
        reward.LeftRerollTime--;
        MarkDirty();
        return (Retcode.RetSucc, isMiracle);
    }

    public MHLHDALAFMJ? PendingRewardProto(bool isMiracle)
    {
        return HasRun ? ToRewardProto(isMiracle ? Run.PendingMiracle : Run.PendingSkill) : null;
    }

    #endregion
}
