using March7thHoney.Data.Activity;
using March7thHoney.Database.Player;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Avatar;
using March7thHoney.GameServer.Game.Activity.FinalityBattle;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Game.Calyx;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Battle;
using March7thHoney.GameServer.Game.Lineup;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.BattleCollege;
using March7thHoney.Proto;
using March7thHoney.Util;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;

namespace March7thHoney.GameServer.Game.Battle;

public class BattleInstance(PlayerInstance player, LineupInfo lineup, List<StageConfigExcel> stages)
    : BasePlayerManager(player)
{
    public BattleInstance(PlayerInstance player, LineupInfo lineup, List<EntityMonster> monsters) : this(player, lineup,
        new List<StageConfigExcel>())
    {
        // A Role Trial battle is fought on the trial's own plane while a trial is running. Its monsters carry the
        // stage's authored event (TrialActivityEntityLoader), which is not always the trial stage id itself
        // (e.g. trial 315120 fights stage 315121, and several trial ids have no StageConfig row at all).
        var trialStageId = player.ActivityManager?.TrialActivityInstance?.Data.CurTrialStageId ?? 0;
        IsTrialActivityBattle = trialStageId != 0 &&
                                player.SceneInstance?.Excel.PlaneType == PlaneTypeEnum.TrialActivity;

        var addedStageIds = new HashSet<int>();

        foreach (var monster in monsters)
        {
            if (!TryResolveStageFromEvent(monster.GetStageId(), player.Data.WorldLevel, out var stage) ||
                stage == null)
                continue;
            if (addedStageIds.Add(stage.StageID))
                Stages.Add(stage);
        }

        // Fallback: some resources use base EventID in monster config while challenge injects custom IDs.
        if (Stages.Count == 0)
            foreach (var monster in monsters)
            {
                if (!TryResolveStageFromEvent(monster.Info.EventID, player.Data.WorldLevel, out var stage) ||
                    stage == null)
                    continue;

                if (addedStageIds.Add(stage.StageID))
                    Stages.Add(stage);
            }

        if (Stages.Count == 0 && IsTrialActivityBattle &&
            GameData.StageConfigData.TryGetValue(trialStageId, out var trialStage))
            Stages.Add(trialStage);

        EntityMonsters = monsters;
        if (Stages.Count > 0)
            StageId = Stages[0].StageID;
    }

    public int BattleId { get; set; } = ++player.NextBattleId;
    public int StaminaCost { get; set; }
    public int WorldLevel { get; set; }
    public int CocoonWave { get; set; }
    // CocoonConfig.ID of a calyx / cavern / echo-of-war battle; 0 for everything else (including farm elements,
    // which also set CocoonWave). Read by the CocoonFinish / FarmTypeCocoonFinish / AnyCocoonFinish handlers.
    public int CocoonId { get; set; }
    public int MappingInfoId { get; set; }
    public int RoundLimit { get; set; }
    public int StageId { get; set; } = stages.Count > 0 ? stages[0].StageID : 0; // Set to 0 when hit monster
    public int EventId { get; set; }
    public int CustomLevel { get; set; }

    /// <summary>
    ///     /spawn custom battle: MonsterConfig IDs per wave. When non-empty these waves replace the stage waves
    ///     (wave IDs 1..N, all on <see cref="StageId" />).
    /// </summary>
    public List<List<int>>? CustomWaves { get; set; }

    /// <summary>/spawn HP override: max and current HP of every battle monster (0 = unchanged).</summary>
    public uint CustomHp { get; set; }

    /// <summary>Every scene monster in this fight was created by /spawn, so it must not advance missions.</summary>
    public bool IsGmSpawnedEncounter => EntityMonsters.Count > 0 && EntityMonsters.All(monster => monster.IsGmSpawned);

    public int? LeaderIndexOverride { get; set; }
    public BattleEndStatus BattleEndStatus { get; set; }

    public List<ItemData> MonsterDropItems { get; set; } = [];

    public List<StageConfigExcel> Stages { get; set; } = stages;
    public LineupInfo Lineup { get; set; } = lineup;
    public List<EntityMonster> EntityMonsters { get; set; } = [];
    public List<AvatarSceneInfo> AvatarInfo { get; set; } = [];
    public List<MazeBuff> Buffs { get; set; } = [];
    public BattleRogueMagicInfo? MagicInfo { get; set; }
    public Dictionary<int, BattleEventInstance> BattleEvents { get; set; } = [];
    public Dictionary<int, BattleTargetList> BattleTargets { get; set; } = [];
    public BattleCollegeConfigExcel? CollegeConfigExcel { get; set; }
    public PVEBattleResultCsReq? BattleResult { get; set; }
    public bool IsTournRogue { get; set; }
    public bool IsTrialActivityBattle { get; set; }
    public GridFightSession? GridFightContext { get; set; }
    public CalyxOverrideContext? CalyxOverride { get; set; }
    public FinalityBattleContext? FinalityContext { get; set; }

    /// <summary>An activity battle fought with its own team: the result must not touch the player's lineups.</summary>
    public bool HasActivityLineup => FinalityContext != null;

    public delegate ValueTask OnBattleEndDelegate(BattleInstance battle, PVEBattleResultCsReq req);

    public event OnBattleEndDelegate? OnBattleEnd;

    public async ValueTask TriggerOnBattleEnd()
    {
        if (OnBattleEnd != null)
            await OnBattleEnd(this, BattleResult!);
    }

    public async Task<ItemList> GetDropItemListAsync()
    {
        if (BattleEndStatus != BattleEndStatus.BattleEndWin) return new ItemList();
        var list = new ItemList();

        foreach (var item in MonsterDropItems) list.ItemList_.Add(item.ToProto());

        foreach (var item in await Player.InventoryManager!.HandleMappingInfo(MappingInfoId, WorldLevel))
            list.ItemList_.Add(item.ToProto());

        if (CollegeConfigExcel == null ||
            Player.BattleCollegeData?.FinishedCollegeIdList.Contains(CollegeConfigExcel.ID) != false)
            return list; // if college excel is not null and college is not finished

        // finish it
        Player.BattleCollegeData.FinishedCollegeIdList.Add(CollegeConfigExcel.ID);
        await Player.SendPacket(new PacketBattleCollegeDataChangeScNotify(Player));
        foreach (var item in await Player.InventoryManager!.HandleReward(CollegeConfigExcel.RewardID))
            list.ItemList_.Add(item.ToProto());

        return list;
    }

    public void AddBattleTarget(int key, int targetId, int progress, int totalProgress = 0)
    {
        if (!BattleTargets.TryGetValue(key, out var value))
        {
            value = new BattleTargetList();
            BattleTargets.Add(key, value);
        }

        var battleTarget = new BattleTarget
        {
            Id = (uint)targetId,
            Progress = (uint)progress,
            TotalProgress = (uint)totalProgress
        };
        value.BattleTargetList_.Add(battleTarget);
    }

    public List<AvatarLineupData> GetBattleAvatars()
    {
        var excel = GameData.StageConfigData[StageId];
        List<int> list = [.. excel.TrialAvatarList];

        // if college excel is not null
        if (CollegeConfigExcel is { TrialAvatarList.Count: > 0 }) list = [.. CollegeConfigExcel.TrialAvatarList];

        // Stage trial lists hold special avatar IDs; the avatar comes from the world-level-0 row.
        list = TrailblazerAvatar.ExcludeOppositeGenderTrials(list, Player.Data.CurrentGender,
            avatar => GameData.SpecialAvatarData.TryGetValue(avatar * 10 + 0, out var specialAvatarExcel)
                ? specialAvatarExcel.AvatarID
                : 0);

        if (list.Count > 0) // if list is not empty
        {
            List<AvatarLineupData> avatars = [];
            foreach (var avatar in list)
            {
                var specialAvatar = Player.AvatarManager!.GetTrialAvatar(avatar);
                if (specialAvatar != null)
                {
                    specialAvatar.CheckLevel(Player.Data.WorldLevel);
                    avatars.Add(new AvatarLineupData(specialAvatar, AvatarType.AvatarTrialType));
                }
                else
                {
                    var avatarInfo = Player.AvatarManager!.GetFormalAvatar(avatar);
                    if (avatarInfo != null) avatars.Add(new AvatarLineupData(avatarInfo, AvatarType.AvatarFormalType));
                }
            }

            return avatars;
        }
        else
        {
            List<AvatarLineupData> avatars = [];
            foreach (var avatar in Lineup.BaseAvatars!) // if list is empty, use scene lineup
            {
                BaseAvatarInfo? avatarInstance = null;
                var avatarType = AvatarType.AvatarFormalType;

                if (avatar.AssistUid != 0)
                {
                    var player = DatabaseHelper.Instance!.GetInstance<AvatarData>(avatar.AssistUid);
                    if (player != null)
                    {
                        avatarInstance = player.FindByAnyId(avatar.BaseAvatarId);
                        avatarType = AvatarType.AvatarAssistType;
                    }
                }
                else if (avatar.SpecialAvatarId != 0)
                {
                    var specialAvatar = Player.AvatarManager!.GetTrialAvatar(avatar.SpecialAvatarId);
                    if (specialAvatar != null)
                    {
                        specialAvatar.CheckLevel(Player.Data.WorldLevel);
                        avatarInstance = specialAvatar;
                        avatarType = AvatarType.AvatarTrialType;
                    }
                }
                else
                {
                    avatarInstance = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                }

                if (avatarInstance == null) continue;

                avatars.Add(new AvatarLineupData(avatarInstance, avatarType, AssistUid: avatar.AssistUid));
            }

            return avatars;
        }
    }

    /// <summary>
    ///     The fight roster the official PVEBattleResultScRsp echoes (battle_avatar_list with the full
    ///     skilltree/equipment payload per avatar - part4.1.json idx 12989/13275). The client's battle-end chain
    ///     for mission fights (client-side TriggerBattle graphs) settles off this result; an empty roster left the
    ///     prologue re-triggering the same battle. Mirrors the non-GridFight branches of <see cref="ToProto" />.
    /// </summary>
    public List<BattleAvatar> BuildResultBattleAvatars()
    {
        var result = new List<BattleAvatar>();
        var collection = new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, Lineup);

        if (CalyxOverride?.LineupOverride is { Count: > 0 } lineupOverride)
        {
            for (var index = 0; index < lineupOverride.Count; index++)
            {
                var avatar = lineupOverride[index];
                var battleAvatar = avatar.AvatarInfo is FormalAvatarInfo formal &&
                                   avatar.BattleAvatarId is int battleAvatarId
                    ? formal.ToBattleProto(collection, battleAvatarId, avatar.AvatarType)
                    : avatar.AvatarInfo.ToBattleProto(collection, avatar.AvatarType);
                battleAvatar.Index = (uint)index;
                result.Add(battleAvatar);
            }

            return result;
        }

        foreach (var avatar in GetBattleAvatars())
            result.Add(ToBattleAvatar(avatar, collection));

        return result;
    }

    /// <summary>
    ///     A support character fights with its owner's light cone and relics; the borrower's inventory would drop
    ///     them or pick an unrelated item with a colliding unique id.
    /// </summary>
    private BattleAvatar ToBattleAvatar(AvatarLineupData avatar, PlayerDataCollection own)
    {
        if (avatar.AssistUid == 0) return avatar.AvatarInfo.ToBattleProto(own, avatar.AvatarType);

        var owner = PlayerData.GetPlayerByUid(avatar.AssistUid);
        var ownerInventory = DatabaseHelper.Instance?.GetInstance<InventoryData>(avatar.AssistUid) ?? new InventoryData();
        var collection = owner != null ? new PlayerDataCollection(owner, ownerInventory, Lineup) : own;
        var proto = avatar.AvatarInfo.ToBattleProto(collection, avatar.AvatarType);
        proto.AssistUid = (uint)avatar.AssistUid;
        return proto;
    }

    public SceneBattleInfo ToProto()
    {
        var proto = new SceneBattleInfo
        {
            BattleId = (uint)BattleId,
            WorldLevel = (uint)WorldLevel,
            RoundsLimit = (uint)RoundLimit,
            StageId = (uint)StageId,
            LogicRandomSeed = (uint)Random.Shared.Next()
        };

        if (MagicInfo != null) proto.BattleRogueMagicInfo = MagicInfo;
        // The classic Rogue battle envelope includes this empty message (official 4.6 seq 15037).
        if (MagicInfo?.OOPCPMANCBO?.RogueMode == 1) proto.BattleActionEventId = new EDCFKACPFNB();

        if (CustomWaves is { Count: > 0 })
        {
            foreach (var waveIds in CustomWaves)
            {
                var wave = new SceneMonsterWave
                {
                    BattleWaveId = (uint)(proto.MonsterWaveList.Count + 1),
                    BattleStageId = (uint)StageId,
                    MonsterParam = new SceneMonsterWaveParam()
                };
                if (CustomLevel > 0) wave.MonsterParam.Level = (uint)CustomLevel;
                foreach (var monsterId in waveIds.Where(id => id > 0))
                    wave.MonsterList.Add(new SceneMonster { MonsterId = (uint)monsterId });

                proto.MonsterWaveList.Add(wave);
            }
        }
        else
        {
            foreach (var protoWave in Stages.Select(wave => wave.ToProto()))
            {
                if (CustomLevel > 0)
                    foreach (var item in protoWave)
                        item.MonsterParam.Level = (uint)CustomLevel;

                proto.MonsterWaveList.AddRange(protoWave);
            }
        }

        if (Player.BattleManager!.NextBattleMonsterIds.Count > 0)
        {
            var ids = Player.BattleManager!.NextBattleMonsterIds;
            // split every 5
            for (var i = 0; i < (ids.Count - 1) / 5 + 1; i++)
            {
                var count = Math.Min(5, ids.Count - i * 5);
                var waveIds = ids.GetRange(i * 5, count);

                proto.MonsterWaveList.Add(new SceneMonsterWave
                {
                    BattleStageId = (uint)(Stages.FirstOrDefault()?.StageID ?? 0),
                    BattleWaveId = (uint)(proto.MonsterWaveList.Count + 1),
                    MonsterParam = new SceneMonsterWaveParam(),
                    MonsterList =
                    {
                        waveIds.Select(x => new SceneMonster
                        {
                            MonsterId = (uint)x
                        })
                    }
                });
            }
        }

        // Both fields, like CalyxOverrideManager.ApplyMonsterHp: the client reads max_hp for the bar and
        // cur_hp for the starting value.
        if (CustomHp > 0)
            foreach (var monster in proto.MonsterWaveList.SelectMany(wave => wave.MonsterList))
            {
                monster.MaxHp = CustomHp;
                monster.CurHp = CustomHp;
            }

        List<AvatarLineupData> avatars;
        if (GridFightContext != null)
        {
            // Presence of the empty event message is significant to the 4.5 battle client.
            proto.BattleActionEventId = new EDCFKACPFNB();
            avatars = GridFightBattleProtoBuilder.Populate(
                this,
                GridFightContext,
                Player.GridFightManager!.Catalog,
                proto);
        }
        else if ((CalyxOverride?.LineupOverride ?? FinalityContext?.LineupOverride) is { Count: > 0 } lineupOverride)
        {
            avatars = [.. lineupOverride];
            var collection = new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, Lineup);
            for (var index = 0; index < lineupOverride.Count; index++)
            {
                var avatar = lineupOverride[index];
                var battleAvatar = avatar.AvatarInfo is FormalAvatarInfo formal &&
                                   avatar.BattleAvatarId is int battleAvatarId
                    ? formal.ToBattleProto(collection, battleAvatarId, avatar.AvatarType)
                    : avatar.AvatarInfo.ToBattleProto(collection, avatar.AvatarType);
                battleAvatar.Index = (uint)index;
                proto.BattleAvatarList.Add(battleAvatar);
            }
        }
        else
        {
            avatars = GetBattleAvatars();
            var own = new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, Lineup);
            foreach (var avatar in avatars)
                proto.BattleAvatarList.Add(ToBattleAvatar(avatar, own));
        }

        // ToProto is synchronous, so the buff preparation is waited on here. AsTask() keeps the blocking wait
        // within the ValueTask contract, and the caller sees the original exception.
        PrepareSceneBuffs(avatars).AsTask().GetAwaiter().GetResult();

        foreach (var buff in Buffs.Clone())
            if (Enum.IsDefined(typeof(DamageTypeEnum), buff.BuffID))
                Buffs.RemoveAll(x => x.BuffID == buff.BuffID && x.DynamicValues.Count == 0);

        foreach (var eventInstance in BattleEvents.Values) proto.BattleEvent.Add(eventInstance.ToProto());

        for (var i = 1; i <= 5; i++)
        {
            var battleTargetEntry = new BattleTargetList();

            if (BattleTargets.TryGetValue(i, out var battleTargetList))
                battleTargetEntry.BattleTargetList_.AddRange(battleTargetList.BattleTargetList_);

            proto.BattleTargetInfo.Add((uint)i, battleTargetEntry);
        }

        var leaderBuff = GameData.MazeBuffData.Values.FirstOrDefault(x =>
            x.InBattleBindingKey == "StageAbility_MazeCommon_EnterBattle_CheckTeamLeader");
        if (leaderBuff != null && Buffs.All(x => x.BuffID != leaderBuff.ID))
        {
            var leaderIndex = LeaderIndexOverride ?? Math.Max(Lineup.GetSlot(Lineup.LeaderAvatarId), 0);
            Buffs.Add(new MazeBuff(leaderBuff.ID, leaderBuff.Lv, leaderIndex)
            {
                WaveFlag = -1
            });
        }

        // Voracity Invasion (贪饕侵蚀): the tier and its buff are determined by StageInvasionConfig/StageInvasionBuff.
        foreach (var stage in Stages)
        {
            if (stage.InvasionMazeBuffID == 0) continue;
            if (Buffs.Any(x => x.BuffID == stage.InvasionMazeBuffID)) continue;

            var level = GameData.MazeBuffData.TryGetValue(stage.InvasionMazeBuffID * 10 + 1, out var mazeBuff)
                ? mazeBuff.Lv
                : 1;
            Buffs.Add(new MazeBuff(stage.InvasionMazeBuffID, level, -1)
            {
                WaveFlag = -1
            });
        }

        // An invaded stage also carries the ally buff "Aha's Big Help" (official: stage 30125121 ships 3034003 and
        // 3034013). Progress is not tracked, so the top tier is used, as the Go server does.
        var allyBuffId = VoracityInvasionAllyBuff.TopBuffId;
        if (allyBuffId != 0 && Stages.Any(stage => stage.InvasionMazeBuffID != 0) &&
            Buffs.All(buff => buff.BuffID != allyBuffId))
            Buffs.Add(new MazeBuff(allyBuffId, 1, -1)
            {
                WaveFlag = -1
            });

        // global buff
        foreach (var buff in GameData.AvatarGlobalBuffConfigData.Values)
            if (Player.AvatarManager!.GetFormalAvatar(buff.AvatarID) != null)
                // add buff
                Buffs.Add(new MazeBuff(buff.MazeBuffID, 1, -1)
                {
                    WaveFlag = -1
                });

        foreach (var buff in Buffs)
        {
            if (buff.WaveFlag != null) continue;
            var buffs = Buffs.FindAll(x => x.BuffID == buff.BuffID);
            if (buffs.Count < 2) continue;
            var count = 0;
            foreach (var mazeBuff in buffs)
            {
                mazeBuff.WaveFlag = (int)Math.Pow(2, count);
                count++;
            }
        }

        CalyxOverride?.Apply(this, proto);

        proto.BuffList.AddRange(Buffs.Select(buff => buff.ToProto(this)));
        return proto;
    }

    private async ValueTask PrepareSceneBuffs(List<AvatarLineupData> avatars)
    {
        var battleAvatarIds = avatars.Select(x => x.AvatarInfo.BaseAvatarId).ToHashSet();
        var battleAvatars = AvatarInfo.Where(x => battleAvatarIds.Contains(x.AvatarInfo.BaseAvatarId)).ToList();
        var battleTargets = EntityMonsters.Cast<BaseGameEntity>().ToList();

        foreach (var avatar in battleAvatars)
        foreach (var modifierName in avatar.Modifiers.ToArray())
        {
            if (!avatar.TryResolveModifier(modifierName, out var abilityList, out var modifier) ||
                modifier.OnBeforeBattle.Count == 0)
                continue;

            await Player.TaskManager!.AbilityLevelTask.TriggerTasks(abilityList, modifier.OnBeforeBattle, avatar,
                battleTargets, new SceneCastSkillCsReq());
        }

        foreach (var monster in EntityMonsters) await monster.ApplyBuff(this);
        foreach (var avatar in battleAvatars) await avatar.ApplyBuff(this);
    }

    public static bool TryResolveStageFromEvent(int eventId, int worldLevel, out StageConfigExcel? stage)
    {
        stage = null;
        if (eventId <= 0) return false;

        if (GameData.PlaneEventData.TryGetValue(eventId * 10 + worldLevel, out var exactPlaneEvent) &&
            GameData.StageConfigData.TryGetValue(exactPlaneEvent.StageID, out stage))
            return true;

        if (GameData.PlaneEventData.TryGetValue(eventId, out var directPlaneEvent) &&
            GameData.StageConfigData.TryGetValue(directPlaneEvent.StageID, out stage))
            return true;

        if (GameData.StageConfigData.TryGetValue(eventId, out stage))
            return true;

        var fallbackPlaneEvent = GameData.PlaneEventData.Values.FirstOrDefault(x => x.EventID == eventId);
        if (fallbackPlaneEvent != null &&
            GameData.StageConfigData.TryGetValue(fallbackPlaneEvent.StageID, out stage))
            return true;

        return false;
    }
}
