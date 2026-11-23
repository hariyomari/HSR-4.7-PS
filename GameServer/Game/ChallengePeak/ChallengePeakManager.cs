using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Challenge;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Game.Challenge.Instances;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.ChallengePeak;
using March7thHoney.Proto;
using March7thHoney.GameServer.Game.Challenge;
using March7thHoney.Util;
using March7thHoney.Enums.Item;

namespace March7thHoney.GameServer.Game.ChallengePeak;

// Challenge instances are owned by ChallengeManager.
public class ChallengePeakManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static bool RequiresKnightClears =>
        ConfigManager.Config.ServerOption.ChallengePeak?.RequireKnightClearsForNormalBoss == true;

    // ChallengeManager owns the player's ChallengeData; every challenge mode reads it through that one instance.
    private ChallengeData ChallengeData => Player.ChallengeManager!.ChallengeData;

    // The King boss (王棋) difficulty is persisted per group: it changes with SetChallengePeakBossHardMode and is not
    // reset by leaving or relogging, matching the official server, which tracks it by peakGroupId.
    public void SetBossHard(int groupId, bool hard)
    {
        var data = ChallengeData;
        data.PeakBossHardByGroup[groupId] = hard;
        DatabaseHelper.MarkDirty(Player.Uid);
        DatabaseHelper.SaveDatabaseType(data);
    }

    public bool IsEasyBossUnlocked(int groupId)
    {
        if (!RequiresKnightClears) return true;
        var group = GameData.ChallengePeakGroupConfigData.GetValueOrDefault(groupId);
        if (group == null) return false;
        var records = ChallengeData.PeakLevelDatas;
        return group.PreLevelIDList.All(id => records.GetValueOrDefault(id)?.HasRecord == true);
    }

    public bool WillUnlockEasyBoss(int groupId, int clearedLevelId)
    {
        if (!RequiresKnightClears || IsEasyBossUnlocked(groupId)) return false;
        var group = GameData.ChallengePeakGroupConfigData.GetValueOrDefault(groupId);
        if (group == null || !group.PreLevelIDList.Contains(clearedLevelId)) return false;
        var records = ChallengeData.PeakLevelDatas;
        return group.PreLevelIDList.All(id => id == clearedLevelId || records.GetValueOrDefault(id)?.HasRecord == true);
    }

    public bool IsBossHard(int groupId) =>
        !IsEasyBossUnlocked(groupId) ||
        ChallengeData.PeakBossHardByGroup.GetValueOrDefault(groupId, false);

    public int GetCurrentGroupId()
    {
        var selected = ChallengeData.CurrentPeakGroupId;
        return GameData.IsChallengePeakGroupAvailable(selected)
            ? selected
            : GameData.GetCurrentChallengePeakGroupId();
    }

    public bool TrySetCurrentGroupId(int groupId)
    {
        if (groupId != 0 && !GameData.IsChallengePeakGroupAvailable(groupId)) return false;
        var data = ChallengeData;
        data.CurrentPeakGroupId = groupId;
        DatabaseHelper.MarkDirty(Player.Uid);
        DatabaseHelper.SaveDatabaseType(data);
        return true;
    }

    public ChallengePeakGroup GetChallengePeakInfo(int groupId, bool? disableHardMode = null)
    {
        var realProgress = RequiresKnightClears;
        var proto = new ChallengePeakGroup
        {
            PeakGroupId = (uint)groupId,
            // Unless given explicitly, send the difficulty remembered for the group: true = normal (blue),
            // false = hard mode (绝境, red).
            DisableHardMode = IsEasyBossUnlocked(groupId) && (disableHardMode ?? !IsBossHard(groupId))
        };

        var data = GameData.ChallengePeakGroupConfigData.GetValueOrDefault(groupId);
        if (data == null) return proto;

        var starNum = 0;
        var clearedPeaks = 0;
        // Container for the best records of the Knight (骑士) stages; the client renders each stage's record (cycles,
        // stars, clearing lineup) from it.
        var records = new ChallengePeakBestRecordList();
        foreach (var levelId in data.PreLevelIDList)
        {
            var levelData = GameData.ChallengePeakConfigData.GetValueOrDefault(levelId);
            if (levelData == null) continue;

            ChallengeData.PeakLevelDatas.TryGetValue(levelId, out var levelPbData);
            var hasRecord = levelPbData?.HasRecord == true;
            if (hasRecord) clearedPeaks++;

            var levelProto = new Proto.ChallengePeak
            {
                PeakId = (uint)levelId,
                HasPassed = !realProgress || hasRecord
            };

            // Default mode keeps the full-star filler values; real-progress mode uses only actual clear records.
            var finishedTargets = hasRecord
                ? levelPbData!.FinishedTargetList
                : realProgress ? [] : levelData.NormalTargetList.Select(x => (uint)x).ToList();
            var recordTeam = hasRecord && levelPbData!.RecordAvatarList is { Count: > 0 }
                ? levelPbData.RecordAvatarList
                : levelPbData?.BaseAvatarList ?? [];

            starNum += finishedTargets.Count;
            levelProto.CyclesUsed = realProgress && !hasRecord ? 0 : levelPbData?.RoundCnt ?? 0;
            levelProto.PeakAvatarIdList.AddRange(levelPbData?.BaseAvatarList ?? []);
            levelProto.FinishedTargetList.AddRange(finishedTargets);
            proto.Peaks.Add(levelProto);

            if (realProgress && !hasRecord) continue;

            var record = new ChallengePeakBestRecord
            {
                PeakId = (uint)levelId,
                CyclesUsed = levelProto.CyclesUsed
            };
            record.FinishedTargetList.AddRange(finishedTargets);
            record.PeakAvatarIdList.AddRange(ToRecordAvatars(recordTeam));
            records.Peaks.Add(record);
        }

        if (records.Peaks.Count > 0) proto.BestRecordList = records;

        // The client reads ObtainedStars as the number of Knight stages cleared; default mode keeps its existing
        // filler value used for unlocking.
        proto.ObtainedStars = (uint)(realProgress ? clearedPeaks : Math.Max(starNum, proto.Peaks.Count * 3));
        proto.CountOfPeaks = (uint)proto.Peaks.Count;
        if (ChallengeData.PeakTakenRewardIds.TryGetValue(groupId, out var takenRewards))
            proto.TakenStarRewards.AddRange(takenRewards.Select(x => (uint)x));

        // boss
        var bossLevelId = data.BossLevelID;
        if (bossLevelId <= 0) return proto;

        var bossLevelData = GameData.ChallengePeakBossConfigData.GetValueOrDefault(bossLevelId);
        if (bossLevelData == null) return proto;

        var bossDatas = ChallengeData.PeakBossLevelDatas;
        bossDatas.TryGetValue((bossLevelId << 2) | 0, out var easyRec);
        bossDatas.TryGetValue((bossLevelId << 2) | 1, out var hardRec);

        var bossProto = new Proto.ChallengePeakBoss
        {
            // The color medal uses the same rule as settlement: a normal clear in zero cycles, or a hard-mode clear.
            HardModeHasPassed = !realProgress || ChallengePeakMedal.HasColorMedal(easyRec, hardRec),
            EasyMode = new ChallengePeakBossClearance { HasPassed = !realProgress || easyRec?.HasRecord == true },
            HardMode = new ChallengePeakBossClearance { HasPassed = !realProgress || hardRec?.HasRecord == true }
        };

        // Both the normal and hard-mode slots are filled with the saved team (own record first, otherwise the other
        // difficulty's), so the same team shows on both difficulties.
        var easySrc = easyRec ?? hardRec;
        var hardSrc = hardRec ?? easyRec;

        if (easySrc != null)
        {
            bossProto.EasyMode.PeakAvatarIdList.AddRange(easySrc.BaseAvatarList);
            bossProto.EasyMode.BuffId = easySrc.BuffId;
        }

        if (hardSrc != null)
        {
            bossProto.HardMode.PeakAvatarIdList.AddRange(hardSrc.BaseAvatarList);
            bossProto.HardMode.BuffId = hardSrc.BuffId;
        }

        // Best records are kept per difficulty with no fallback to the other one; otherwise normal and hard-mode
        // records would leak into each other.
        FillBossRecord(bossProto.EasyMode, easyRec, bossLevelId, bossLevelData, false);
        FillBossRecord(bossProto.HardMode, hardRec, bossLevelId, bossLevelData, true);

        // The client uses this list only to render the normal-difficulty stars (hard mode relies on
        // hardModeHasPassed); without a real record the unlock seed is kept.
        HashSet<uint> easyTargets = [];
        if (easyRec?.HasRecord == true)
        {
            foreach (var t in easyRec.FinishedTargetList) easyTargets.Add(t);
        }
        else if (!realProgress)
        {
            // No real clearance record yet: seed normal + hard targets so the boss reads as cleared on both difficulties.
            if (GameData.ChallengePeakConfigData.TryGetValue(bossLevelId, out var bossPeakCfg))
                foreach (var t in bossPeakCfg.NormalTargetList)
                    easyTargets.Add((uint)t);
            if (bossLevelData.HardTarget > 0) easyTargets.Add((uint)bossLevelData.HardTarget);
        }

        bossProto.FinishedTargetList.AddRange(easyTargets);
        proto.PeakBoss = bossProto;

        return proto;
    }

    // Converts base avatar ids into record lineup entries that carry the skin.
    private IEnumerable<ChallengePeakRecordAvatar> ToRecordAvatars(IEnumerable<uint> avatarIds)
    {
        foreach (var avatarId in avatarIds)
        {
            var avatar = Player.AvatarManager!.GetFormalAvatar((int)avatarId);
            yield return new ChallengePeakRecordAvatar
            {
                AvatarId = avatarId,
                SkinId = (uint)(avatar?.GetCurPathInfo().Skin ?? 0)
            };
        }
    }

    // Turn budget of the King boss: normal takes the most lenient normal target, hard mode takes the hard target. This
    // agrees with the official maxLeftTurn of 6 on normal and 2 on hard mode.
    private static int GetBossTurnBudget(int bossLevelId, ChallengePeakBossConfigExcel bossExcel, bool isHard)
    {
        if (isHard) return GameData.BattleTargetConfigData.GetValueOrDefault(bossExcel.HardTarget)?.TargetParam ?? 0;
        if (!GameData.ChallengePeakConfigData.TryGetValue(bossLevelId, out var bossPeakCfg)) return 0;
        return bossPeakCfg.NormalTargetList
            .Select(t => GameData.BattleTargetConfigData.GetValueOrDefault(t)?.TargetParam ?? 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private void FillBossRecord(ChallengePeakBossClearance clearance, ChallengePeakBossLevelData? rec,
        int bossLevelId, ChallengePeakBossConfigExcel bossExcel, bool isHard)
    {
        if (rec?.HasRecord != true) return;

        clearance.BestCycleCount = rec.RoundCnt;
        clearance.BestRecordBuffId = rec.RecordBuffId;
        clearance.MaxLeftTurn =
            (uint)Math.Max(0, GetBossTurnBudget(bossLevelId, bossExcel, isHard) - (int)rec.RoundCnt);
        clearance.BestRecordAvatarList.AddRange(
            ToRecordAvatars(rec.RecordAvatarList is { Count: > 0 } ? rec.RecordAvatarList : rec.BaseAvatarList));
    }

    public async ValueTask<List<ChallengePeakRewardGroup>?> TakeRewards(int groupId, IEnumerable<uint> requestRewardIds)
    {
        if (!GameData.ChallengePeakGroupConfigData.TryGetValue(groupId, out var groupConfig)) return null;
        if (!GameData.ChallengePeakRewardData.TryGetValue(groupConfig.RewardGroupID, out var rewardLine)) return null;

        var passedMobs = groupConfig.PreLevelIDList.Count;
        var mobStars = passedMobs * 3;
        var hasBoss = groupConfig.BossLevelID > 0;
        var bossStars = hasBoss ? 3 : 0;

        // Real-progress mode pays only what the records earned; the default mode keeps the optimistic values.
        if (RequiresKnightClears)
        {
            var data = ChallengeData;
            var passedLevels = groupConfig.PreLevelIDList
                .Select(id => data.PeakLevelDatas.GetValueOrDefault(id))
                .Where(level => level?.HasRecord == true)
                .ToList();
            passedMobs = passedLevels.Count;
            mobStars = passedLevels.Sum(level => level!.FinishedTargetList.Count);

            data.PeakBossLevelDatas.TryGetValue((groupConfig.BossLevelID << 2) | 0, out var easyRec);
            bossStars = easyRec?.HasRecord == true ? easyRec.FinishedTargetList.Count : 0;
        }

        if (!ChallengeData.PeakTakenRewardIds.TryGetValue(groupId, out var takenList))
        {
            takenList = [];
            ChallengeData.PeakTakenRewardIds[groupId] = takenList;
        }

        var requested = requestRewardIds.Select(x => (int)x).ToHashSet();
        if (requested.Count == 0) requested = rewardLine.Select(x => x.ID).ToHashSet();

        var rspGroups = new List<ChallengePeakRewardGroup>();
        var gainItems = new List<ItemData>();

        foreach (var entry in rewardLine.OrderBy(x => x.ID))
        {
            if (!requested.Contains(entry.ID)) continue;
            if (takenList.Contains(entry.ID)) continue;

            // The limited-time head frames (BOSS_STAR_LIMIT_REWARD / BOSS_COLOR_TARGET_REWARD) cannot be claimed by the
            // client; they are granted only at King boss settlement, according to the record.
            var passed = entry.RewardType switch
            {
                "MOB_PASS_REWARD" => passedMobs >= entry.TypeValue,
                "MOB_STAR_REWARD" => mobStars >= entry.TypeValue,
                "BOSS_STAR_REWARD" => bossStars >= entry.TypeValue,
                _ => false
            };
            if (!passed) continue;

            if (!GameData.RewardDataData.TryGetValue(entry.RewardID, out var rewardExcel)) continue;

            var group = new ChallengePeakRewardGroup
            {
                RewardId = (uint)entry.ID,
                Reward = new ItemList()
            };

            if (rewardExcel.Hcoin > 0)
            {
                var hcoin = new ItemData { ItemId = (int)VirtualItemEnum.Hcoin, Count = rewardExcel.Hcoin };
                gainItems.Add(hcoin);
                group.Reward.ItemList_.Add(hcoin.ToProto());
            }

            foreach (var (itemId, count) in rewardExcel.GetItems())
            {
                var item = new ItemData
                {
                    ItemId = itemId,
                    Count = count
                };
                gainItems.Add(item);
                group.Reward.ItemList_.Add(item.ToProto());
            }

            takenList.Add(entry.ID);
            rspGroups.Add(group);
        }

        if (gainItems.Count > 0) await Player.InventoryManager!.AddItems(gainItems);
        if (rspGroups.Count > 0) DatabaseHelper.MarkDirty(Player.Uid);
        return rspGroups;
    }

    private int GetObtainedStars(ChallengePeakGroupConfigExcel groupConfig)
    {
        var stars = 0;
        foreach (var levelId in groupConfig.PreLevelIDList)
            if (ChallengeData.PeakLevelDatas.TryGetValue(levelId, out var levelData))
                stars += (int)levelData.PeakStar;

        if (groupConfig.BossLevelID > 0)
        {
            var easyKey = (groupConfig.BossLevelID << 2) | 0;
            var hardKey = (groupConfig.BossLevelID << 2) | 1;
            if (ChallengeData.PeakBossLevelDatas.TryGetValue(easyKey, out var easyData))
                stars += (int)easyData.PeakStar;
            if (ChallengeData.PeakBossLevelDatas.TryGetValue(hardKey, out var hardData))
                stars += (int)hardData.PeakStar;
        }

        return stars;
    }

    public async ValueTask SetLineupAvatars(int groupId, List<ChallengePeakLineup> lineups)
    {
        var datas = ChallengeData.PeakLevelDatas;
        foreach (var lineup in lineups)
        {
            List<uint> avatarIds = [];

            foreach (var avatarId in lineup.PeakAvatarIdList.ToList())
            {
                var avatar = Player.AvatarManager!.GetFormalAvatar((int)avatarId);
                if (avatar != null)
                    avatarIds.Add((uint)avatar.BaseAvatarId);
            }

            // Only the last-used lineup is updated; the stage's existing best record is kept.
            if (!datas.TryGetValue((int)lineup.PeakId, out var levelData))
            {
                levelData = new ChallengePeakLevelData { LevelId = (int)lineup.PeakId };
                datas[(int)lineup.PeakId] = levelData;
            }

            levelData.BaseAvatarList = avatarIds;
        }

        DatabaseHelper.MarkDirty(Player.Uid);
        await Player.SendPacket(new PacketChallengePeakGroupDataUpdateScNotify(GetChallengePeakInfo(groupId)));
    }

    // Better means more targets finished, or the same number of targets in fewer cycles.
    private static bool IsBetterRecord(bool hasRecord, int oldTargetCount, uint oldRoundCnt, int newTargetCount,
        uint newRoundCnt)
    {
        if (!hasRecord) return true;
        if (newTargetCount != oldTargetCount) return newTargetCount > oldTargetCount;
        return newRoundCnt < oldRoundCnt;
    }

    public async ValueTask SaveHistory(ChallengePeakInstance inst, List<uint> targetIds)
    {
        var peak = inst.Data.Peak!;
        var team = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.Select(x => (uint)x.BaseAvatarId).ToList() ?? [];
        var roundCnt = peak.RoundCnt;

        if (inst.Config.BossExcel != null)
        {
            // is hard
            var isHard = peak.IsHard;
            var levelId = ((int)peak.CurrentPeakLevelId << 2) | (isHard ? 1 : 0);
            var buffId = peak.Buffs.FirstOrDefault();

            var bossDatas = ChallengeData.PeakBossLevelDatas;
            if (!bossDatas.TryGetValue(levelId, out var data))
            {
                data = new ChallengePeakBossLevelData { LevelId = (int)peak.CurrentPeakLevelId, IsHard = isHard };
                bossDatas[levelId] = data;
            }

            // The last-used lineup and buff are refreshed every time.
            data.BaseAvatarList = team;
            data.BuffId = buffId;

            // The best record is written only when it is beaten, and is not mirrored across difficulties.
            if (IsBetterRecord(data.HasRecord, data.FinishedTargetList.Count, data.RoundCnt, targetIds.Count,
                    roundCnt))
            {
                data.HasRecord = true;
                data.RoundCnt = roundCnt;
                data.PeakStar = (uint)targetIds.Count;
                data.FinishedTargetList = targetIds;
                data.RecordAvatarList = [.. team];
                data.RecordBuffId = buffId;
            }

            // Mirror the latest team and buff into the other difficulty's record so that both the normal and hard-mode
            // slots show the team from the last attempt (the client slot only reads easyMode).
            if (bossDatas.TryGetValue(levelId ^ 1, out var mirror))
            {
                mirror.BaseAvatarList = data.BaseAvatarList;
                mirror.BuffId = data.BuffId;
            }

            await GrantBossHeadFrame((int)peak.CurrentPeakGroupId, (int)peak.CurrentPeakLevelId);
        }
        else
        {
            // Save level data
            var levelId = (int)peak.CurrentPeakLevelId;

            var levelDatas = ChallengeData.PeakLevelDatas;
            if (!levelDatas.TryGetValue(levelId, out var data))
            {
                data = new ChallengePeakLevelData { LevelId = levelId };
                levelDatas[levelId] = data;
            }

            data.BaseAvatarList = team;

            if (IsBetterRecord(data.HasRecord, data.FinishedTargetList.Count, data.RoundCnt, targetIds.Count,
                    roundCnt))
            {
                data.HasRecord = true;
                data.RoundCnt = roundCnt;
                data.PeakStar = (uint)targetIds.Count;
                data.FinishedTargetList = targetIds;
                data.RecordAvatarList = [.. team];
            }
        }

        DatabaseHelper.MarkDirty(Player.Uid);
        // Only King boss runs have a difficulty. IsHard is always false for Knight runs, so they must use the
        // difficulty remembered for the group; otherwise the overview flips back to normal.
        var disableHardMode = inst.Config.BossExcel != null ? !peak.IsHard : (bool?)null;
        await Player.SendPacket(
            new PacketChallengePeakGroupDataUpdateScNotify(
                GetChallengePeakInfo((int)peak.CurrentPeakGroupId, disableHardMode)));
    }

    // The limited-time head frame is read from ChallengePeakReward by the medal rank of the King boss best records. It
    // only upgrades, never downgrades, and the same rank is not granted twice.
    private async ValueTask GrantBossHeadFrame(int groupId, int bossLevelId)
    {
        if (!GameData.ChallengePeakGroupConfigData.TryGetValue(groupId, out var groupConfig)) return;

        var bossDatas = ChallengeData.PeakBossLevelDatas;
        bossDatas.TryGetValue(bossLevelId << 2, out var easyRec);
        bossDatas.TryGetValue((bossLevelId << 2) | 1, out var hardRec);

        var rank = ChallengePeakMedal.GetBossRank(easyRec, hardRec);
        if (rank <= ChallengePeakMedal.GetHeadFrameRank(Player.Data.HeadFrame.HeadFrameId)) return;

        var frameId = ChallengePeakMedal.GetHeadFrameItemId(groupConfig.RewardGroupID, rank);
        if (frameId == 0) return;

        // The server has no season end time, so a far-future expiry stands in for "valid this season". The item also
        // goes into the inventory; only then does the client offer the display toggle.
        await HeadFrameService.Grant(Player, frameId, PlayerHeadFrameInfo.NoSeasonExpireTime);
    }

    public async ValueTask<Retcode> StartChallenge(int levelId, uint buffId, List<int> avatarIdList)
    {
        // Get challenge excel
        if (!GameData.ChallengePeakConfigData.TryGetValue(levelId, out var excel))
            return Retcode.RetChallengeNotExist;

        // Format to base avatar id
        List<int> avatarIds = [];
        foreach (var avatarId in avatarIdList)
        {
            var avatar = Player.AvatarManager!.GetFormalAvatar(avatarId);
            if (avatar != null)
                avatarIds.Add(avatar.BaseAvatarId);
        }

        // Get lineup
        var lineup = Player.LineupManager!.GetExtraLineup(ExtraLineupType.LineupChallenge);
        if (lineup == null)
        {
            // Ensure challenge extra lineup exists before filling avatars.
            Player.LineupManager.SetExtraLineup(ExtraLineupType.LineupChallenge, []);
            lineup = Player.LineupManager.GetExtraLineup(ExtraLineupType.LineupChallenge);
        }

        if (lineup == null)
            return Retcode.RetChallengeLineupEmpty;
        if (avatarIds.Count > 0)
            lineup.BaseAvatars = avatarIds.Select(x => new LineupAvatarInfo
            {
                BaseAvatarId = x
            }).ToList();
        else
            lineup.BaseAvatars = ChallengeData.PeakLevelDatas.GetValueOrDefault(levelId)
                ?.BaseAvatarList
                .Select(x => new LineupAvatarInfo
                {
                    BaseAvatarId = (int)x
                }).ToList() ?? [];

        // Set technique points to full
        lineup.Mp = 8; // Max Mp

        // Make sure this lineup has avatars set
        if (Player.AvatarManager!.Data.FormalAvatars.Count == 0)
            return Retcode.RetChallengeLineupEmpty;

        // Reset hp/sp
        foreach (var avatar in Player.AvatarManager!.Data.FormalAvatars)
        {
            avatar.SetCurHp(10000, true);
            avatar.SetCurSp(5000, true);
        }

        // Set challenge data for player
        var data = new ChallengeStateData
        {
            Peak = new PeakChallengeState
            {
                CurrentPeakGroupId = (uint)(GameData.ChallengePeakGroupConfigData.Values
                    .FirstOrDefault(x => x.BossLevelID == levelId || x.PreLevelIDList.Contains(levelId))?.ID ??
                    GetCurrentGroupId()),
                CurrentPeakLevelId = (uint)levelId,
                CurrentExtraLineup = ChallengeLineupType.Challenge1,
                CurStatus = 1
            }
        };

        if (excel.BossExcel != null)
            data.Peak!.IsHard = IsBossHard((int)data.Peak!.CurrentPeakGroupId);

        if (buffId > 0) data.Peak!.Buffs.Add(buffId);

        // Persist the King boss team and buff as soon as the scene is entered (no clear required), mirrored to the
        // other difficulty's key (the client slot only reads easyMode).
        if (excel.BossExcel != null)
        {
            var team = lineup.BaseAvatars.Select(x => (uint)x.BaseAvatarId).ToList();
            if (team.Count > 0)
            {
                var bossKey = (levelId << 2) | (data.Peak!.IsHard ? 1 : 0);
                var bossDatas = ChallengeData.PeakBossLevelDatas;
                if (!bossDatas.TryGetValue(bossKey, out var rec))
                    rec = new ChallengePeakBossLevelData { LevelId = levelId, IsHard = data.Peak!.IsHard };
                rec.BaseAvatarList = team;
                rec.BuffId = buffId;
                bossDatas[bossKey] = rec;
                if (bossDatas.TryGetValue(bossKey ^ 1, out var mirror)) { mirror.BaseAvatarList = team; mirror.BuffId = buffId; }
            }
        }

        var instance = new ChallengePeakInstance(Player, data);

        Player.ChallengeManager!.ChallengeInstance = instance;

        // Set first lineup before we enter scenes
        await Player.LineupManager!.SetExtraLineup((ExtraLineupType)instance.Data.Peak!.CurrentExtraLineup);

        // Enter scene
        try
        {
            var mapEntranceId = excel.GetMapEntranceId();
            if (mapEntranceId <= 0)
            {
                mapEntranceId = GameData.ChallengePeakGroupConfigData.Values
                    .FirstOrDefault(x => x.BossLevelID == levelId || x.PreLevelIDList.Contains(levelId))
                    ?.MapEntranceID ?? 0;
            }

            if (mapEntranceId <= 0)
                return Retcode.RetChallengeNotExist;

            await Player.EnterScene(mapEntranceId, 0, true);
        }
        catch
        {
            // Reset lineup/instance if entering scene failed
            Player.ChallengeManager!.ChallengeInstance = null;
            return Retcode.RetChallengeNotExist;
        }

        // Save start positions
        data.Peak!.StartPos = Player.Data.Pos!;
        data.Peak!.StartRot = Player.Data.Rot!;
        data.Peak!.SavedMp = (uint)Player.LineupManager.GetCurLineup()!.Mp;

        // A King boss run sends its difficulty on entry; the client uses it to render the red hard-mode settlement
        // screen. Knight runs have no difficulty and use the one remembered for the group.
        var disableHardMode = excel.BossExcel != null ? !data.Peak!.IsHard : (bool?)null;
        await Player.SendPacket(new PacketChallengePeakGroupDataUpdateScNotify(
            GetChallengePeakInfo((int)data.Peak!.CurrentPeakGroupId, disableHardMode)));

        // Save instance
        Player.ChallengeManager!.SaveInstance(instance);
        return Retcode.RetSucc;
    }

    // Retry (重新挑战) from the settlement screen: rebuild the run from the current instance's level, buff and team. A
    // King boss run keeps its difficulty because IsBossHard reads it per group.
    public async ValueTask<Retcode> Restart()
    {
        if (Player.ChallengeManager!.ChallengeInstance is not ChallengePeakInstance inst)
            return Retcode.RetChallengeNotExist;

        var levelId = inst.Config.ID;
        var buffId = inst.Data.Peak!.Buffs.FirstOrDefault();
        var avatarIds = (Player.LineupManager!.GetExtraLineup(ExtraLineupType.LineupChallenge)?.BaseAvatars ?? [])
            .Select(x => x.BaseAvatarId).ToList();

        Player.ChallengeManager!.ClearInstance();
        return await StartChallenge(levelId, buffId, avatarIds);
    }
}
