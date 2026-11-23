using March7thHoney.Enums.Mission;
using System.Collections.Concurrent;
using March7thHoney.Data;
using March7thHoney.Data.Config;
using March7thHoney.Data.Config.Task;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Drop;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Game.LevelTasks.AvatarTask;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Battle;

public class SceneSkillManager(PlayerInstance player) : BasePlayerManager(player)
{
    private const int SpeedDestructEventId = 10003;

    public async ValueTask<SkillResultData> OnCast(SceneCastSkillCsReq req)
    {
        Player.TaskManager!.AbilityLevelTask.SummonAttackDetectBuffs.Clear();
        var handledDestruct = await HandleDestructPropDrops(req);
        if (Player.SceneInstance is not { } scene)
            return new SkillResultData(Retcode.RetSceneEntityNotExist);

        // attacked_by_entity_id identifies the attack source; cast_entity_id correlates the cast.
        // Official 4.6 seq 9469-9471 reports a monster source with no hit targets and receives only
        // an ACK. Substituting the leader here starts an unexpected battle during the ramp cinematic.
        var attackEntity = scene.Entities.GetValueOrDefault((int)req.AttackedByEntityId);

        if (attackEntity == null)
            return handledDestruct
                ? new SkillResultData(Retcode.RetSucc)
                : new SkillResultData(Retcode.RetSceneEntityNotExist);

        List<BaseGameEntity> targetEntities = [];
        HashSet<int> targetIds = [];
        var assistIds = req.AssistMonsterEntityIdList.Concat(req.AssistMonsterEntityInfo.SelectMany(info => info.EntityIdList));
        foreach (var id in assistIds)
            if (scene.Entities.TryGetValue((int)id, out var target) && targetIds.Add(target.EntityId))
                targetEntities.Add(target);

        // A projectile impact can report its monster through hit_target_entity_id_list alone.
        if (attackEntity is AvatarSceneInfo && req.SkillExtraTags.Contains(SkillExtraTag.SceneCastSkillProjectileHit) &&
            targetEntities.All(entity => entity is not EntityMonster { IsAlive: true }))
        {
            foreach (var id in req.HitTargetEntityIdList)
                if (scene.Entities.GetValueOrDefault((int)id) is EntityMonster target && targetIds.Add(target.EntityId))
                    targetEntities.Add(target);
        }

        if (attackEntity is EntityMonster &&
            !targetEntities.Any(entity => scene.AvatarInfo.ContainsKey(entity.EntityId)))
            return new SkillResultData(Retcode.RetSucc);

        await TriggerAttackDetectSummons(targetEntities);

        // get ability file
        var abilities = GetAbilityConfig(attackEntity);

        // Fallback path for avatars whose ConfigAdventureAbility/LocalPlayer/<Name>_00_Ability.json
        // hasn't been dumped from the client yet (e.g. brand-new 4.3 characters before dim picks them up).
        // We can't run the ability's OnStart task chain without that file, but the cast itself can still
        // resolve: trigger battle when assist monsters were reported by the client, and let the
        // downstream maze-buff backfill (below) bind the avatar's SkillMaze maze buffs. No avatar / monster
        // id is hard-coded — selection is purely data-driven from req.AssistMonster* and entity types.
        AbilityLevelResult res;
        if (abilities == null || abilities.AbilityList.Count < 1)
        {
            res = await BuildAbilitylessCast(attackEntity, targetEntities, req);
        }
        else
        {
            var abilityName = !string.IsNullOrEmpty(req.MazeAbilityStr) ? req.MazeAbilityStr :
                req.SkillIndex == 0 ? "NormalAtk01" : "MazeSkill";
            var targetAbility = abilities.AbilityList.Find(x => x.Name.Contains(abilityName));
            if (targetAbility == null)
            {
                targetAbility = abilities.AbilityList.FirstOrDefault();
                if (targetAbility == null)
                    return new SkillResultData(Retcode.RetMazeNoAbility);
            }

            // execute ability
            res = await Player.TaskManager!.AbilityLevelTask.TriggerTasks(abilities, targetAbility.OnStart,
                attackEntity, targetEntities, req);
        }

        // check if avatar execute
        if (attackEntity is AvatarSceneInfo) await scene.OnUseSkill(req);

        // backfill character maze buffs for techniques whose ability JSON spawns
        // a summon unit instead of calling AddMazeBuff (e.g. Dahlia 1321 -> 132101,
        // Silver Wolf lv.999 1506 -> 150601 which uses CharacterSkillAdv binding).
        // candidates = AvatarMazeBuff entries bound to the SkillMaze slot via either
        // CharacterSkill or CharacterSkillAdv. If the ability JSON already added one
        // of them, skip entirely; otherwise apply the canonical primary entry
        // (UseType SummonUnit/AddBattleBuff/TriggerBattle) so battleInfo.buffList
        // picks it up. This keeps multi-entry avatars (Cyrene, Cerydra, Robin...)
        // from receiving stray summon/listener buffs.
        if (req.SkillIndex == 1 && attackEntity is AvatarSceneInfo casterAvatar)
        {
            // Multi-path avatars look up technique buffs by their current path id (8005/1224); the owner still uses the
            // base id so the slot resolves.
            var pathAvatarId = casterAvatar.AvatarInfo.AvatarId;
            var candidates = GameData.MazeBuffData
                .Where(kvp => kvp.Key / 10 / 100 == pathAvatarId
                              && kvp.Value.MazeBuffType == "Character"
                              && (kvp.Value.InBattleBindingType == "CharacterSkill"
                                  || kvp.Value.InBattleBindingType == "CharacterSkillAdv")
                              && kvp.Value.InBattleBindingKey == "SkillMaze")
                .Select(kvp => (BuffId: kvp.Key / 10, Excel: kvp.Value))
                .ToList();

            // drop buffs this avatar's ability already delivers to enemies via attack-detect; they must not also be applied to the caster (e.g. Gilgamesh 150901 would freeze the caster)
            var detectBuffs = Player.TaskManager!.AbilityLevelTask.SummonAttackDetectBuffs;
            candidates = candidates.Where(c => !detectBuffs.Contains(c.BuffId)).ToList();

            // Enemy time-stop effects may only be delivered by a field trigger; never backfill them onto the caster.
            candidates = candidates.Where(c =>
                !GameData.AdventureModifierData.TryGetValue(c.Excel.ModifierName, out var modifier)
                || !modifier.BehaviorFlagList.Contains("TimeLock", StringComparer.OrdinalIgnoreCase)).ToList();

            // Attack techniques can apply their opening buff to the hit monster in the OnBattle task.
            var casterId = casterAvatar.AvatarInfo.BaseAvatarId;
            var enemyBuffs = targetEntities.OfType<EntityMonster>().SelectMany(monster => monster.BuffList);
            var alreadyApplied = candidates.Any(c => casterAvatar.BuffList.Any(b => b.BuffId == c.BuffId) ||
                enemyBuffs.Any(b => b.BuffId == c.BuffId && b.OwnerAvatarId == casterId));

            if (!alreadyApplied)
            {
                var primary = candidates.FirstOrDefault(c =>
                    c.Excel.UseType is "SummonUnit" or "AddBattleBuff" ||
                    (res.Instance != null && c.Excel.UseType == "TriggerBattle"));
                if (primary.BuffId != 0)
                    await casterAvatar.AddBuff(new SceneBuff(primary.BuffId, primary.Excel.Lv, casterId));
            }

            // Listener buffs charged by teammates' techniques (e.g. March 7th (The Hunt) 122401) are identified from
            // data: each technique a teammate uses adds one stack to the holder, up to the cap.
            await StackTeammateTechniqueBuffs(casterAvatar);
        }

        // An ambush rolls the attacking monster into combat, not the avatar it hit.
        // Official 4.6 seq 30233-30239 uses the monster id in monster_battle_info.
        if (attackEntity is EntityMonster attacker && res.Instance != null)
            return new SkillResultData(Retcode.RetSucc, res.Instance,
                [new HitMonsterInstance(attacker.EntityId, MonsterBattleType.TriggerBattle)]);

        // Projectile hit and lifetime callbacks can report the same monster in one cast.
        return new SkillResultData(Retcode.RetSucc, res.Instance, res.BattleInfos?.DistinctBy(hit => hit.MonsterId).ToList());
    }

    private async ValueTask TriggerAttackDetectSummons(List<BaseGameEntity> targetEntities)
    {
        if (Player.SceneInstance == null || targetEntities.Count == 0) return;

        foreach (var summon in Player.SceneInstance.SummonUnit.Values)
        {
            var center = summon.Motion.Pos;
            if (center == null) continue;

            foreach (var trigger in summon.TriggerList)
            {
                if (!trigger.OnTriggerEnter.Any(x => x is AddMazeBuff)) continue;

                var radius = trigger.Radius.GetValue() * 1000L;
                if (radius <= 0) continue;

                var targetIds = targetEntities.OfType<EntityMonster>()
                    .Where(x => x.IsAlive && GetDistanceSquared(x.Position, center) <= radius * radius)
                    .Select(x => (uint)x.EntityId)
                    .ToList();
                if (targetIds.Count > 0)
                    await Player.SceneInstance.TriggerSummonUnit(summon.EntityId, trigger.TriggerName, targetIds);
            }
        }
    }

    private static long GetDistanceSquared(Position position, Vector center)
    {
        var x = (long)position.X - center.X;
        var z = (long)position.Z - center.Z;
        return x * x + z * z;
    }

    private async ValueTask<bool> HandleDestructPropDrops(SceneCastSkillCsReq req)
    {
        if (Player.SceneInstance == null || Player.InventoryManager == null) return false;

        var handled = false;
        var destroyed = 0;

        HashSet<int> hitEntityIds = [(int)req.AttackedByEntityId];
        foreach (var id in req.AssistMonsterEntityIdList)
            hitEntityIds.Add((int)id);

        foreach (var info in req.AssistMonsterEntityInfo)
        foreach (var id in info.EntityIdList)
            hitEntityIds.Add((int)id);

        foreach (var entityId in hitEntityIds)
        {
            if (!Player.SceneInstance.Entities.TryGetValue(entityId, out var entity)) continue;
            if (entity is not EntityProp prop) continue;
            if (prop.Excel.PropType != PropTypeEnum.PROP_DESTRUCT) continue;
            handled = true;
            destroyed++;

            if (prop.Excel.IsMpRecover)
            {
                await Player.LineupManager!.GainMp(2, true, SyncLineupReason.SyncReasonMpAddPropHit);
            }
            else if (prop.Excel.IsHpRecover)
            {
                Player.LineupManager!.GetCurLineup()!.Heal(GameConstants.PROP_HP_RECOVER, false);
                await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager.GetCurLineup()!));
            }

            if (prop.PropInfo.EventID > 0)
            {
                await ApplyPropEventEffect(prop, req);

                var eventItems = BuildPlaneEventRewards(prop.PropInfo.EventID);
                if (eventItems.Count > 0)
                {
                    await Player.InventoryManager.AddItems(eventItems);
                }
                else
                {
                    // Client still expects a plane-event notify for special destructibles with no item reward.
                    await Player.SendPacket(new PacketScenePlaneEventScNotify(new ItemList()));
                }
            }

            if (prop.PropInfo.ChestID > 0)
            {
                var items = DropService.CalculateDropsFromProp(prop.PropInfo.ChestID);
                if (items.Count > 0) await Player.InventoryManager.AddItems(items);
            }

            // Destruct props should disappear from scene instead of being refreshed as opened state.
            await Player.SceneInstance.RemoveEntity(prop, false);
            await Player.SendPacket(new PacketSceneGroupRefreshScNotify(
                Player,
                null,
                prop,
                SceneGroupRefreshType.Afibfmafncc));

            // Persist as opened for non-reset groups so relog won't respawn it.
            if (prop.Group.SaveType != SaveTypeEnum.Reset && Player.SceneInstance.PersistsPropState)
                Player.SetScenePropData(Player.SceneInstance.FloorId, prop.GroupId, prop.PropInfo.ID, PropStateEnum.Open);
        }

        if (destroyed > 0)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.PropDestruct, destroyed);
        return handled;
    }

    private async ValueTask ApplyPropEventEffect(EntityProp prop, SceneCastSkillCsReq req)
    {
        if (prop.PropInfo.EventID != SpeedDestructEventId) return;
        if (Player.SceneInstance == null) return;

        var casterEntity = Player.SceneInstance.Entities.GetValueOrDefault((int)req.AttackedByEntityId);
        if (casterEntity is not AvatarSceneInfo casterAvatar) return;

        await casterAvatar.AddBuff(new SceneBuff(
            SceneCompatRules.SpeedDestructBuffId,
            1,
            casterAvatar.AvatarInfo.BaseAvatarId,
            SceneCompatRules.SpeedDestructBuffDurationSec));
    }

    private List<ItemData> BuildPlaneEventRewards(int eventId)
    {
        List<ItemData> items = [];
        GameData.PlaneEventData.TryGetValue(eventId * 10 + Player.Data.WorldLevel, out var planeEvent);
        if (planeEvent == null) return items;

        AddRewardItems(planeEvent.Reward, items);
        foreach (var dropRewardId in planeEvent.DropList) AddRewardItems(dropRewardId, items);

        return items;
    }

    private static void AddRewardItems(int rewardId, List<ItemData> items)
    {
        GameData.RewardDataData.TryGetValue(rewardId, out var reward);
        if (reward == null) return;

        if (reward.Hcoin > 0)
            items.Add(new ItemData
            {
                ItemId = (int)VirtualItemEnum.Hcoin,
                Count = reward.Hcoin
            });

        foreach (var (itemId, count) in reward.GetItems())
            items.Add(new ItemData
            {
                ItemId = itemId,
                Count = count
            });
    }

    // Fallback for avatars missing an AdventureAbility JSON: mirror the minimal contract that
    // AbilityLevelTask.AdventureTriggerAttack would have produced — open-world battle is triggered
    // when the client reported assist monsters (普攻命中怪), and HitMonsterInstance entries are
    // emitted for every reported entity so the client knows which ones to roll into the battle.
    // No avatar / monster / entity id is hard-coded: selection is purely from req + entity types.
    private async ValueTask<AbilityLevelResult> BuildAbilitylessCast(
        BaseGameEntity attackEntity,
        List<BaseGameEntity> targetEntities,
        SceneCastSkillCsReq req)
    {
        if (targetEntities.Count == 0)
            return new AbilityLevelResult();

        // A monster projectile can report HIT and LIFETIME_FINISH together when it hits an avatar.
        // The avatar technique lifetime rule below does not apply to this confirmed ambush.
        if (attackEntity is EntityMonster monster)
        {
            var ambush = await Player.BattleManager!.StartBattle(monster, targetEntities, false);
            return ambush == null
                ? new AbilityLevelResult()
                : new AbilityLevelResult(ambush,
                    [new HitMonsterInstance(monster.EntityId, MonsterBattleType.TriggerBattle)]);
        }

        // A technique ending its projectile lifetime can be a persistent-field cast. A normal attack
        // can report both HIT and LIFETIME_FINISH and still enters battle when it hits a live monster.
        if (req.SkillIndex != 0 && req.SkillExtraTags.Contains(SkillExtraTag.SceneCastSkillProjectileLifetimeFinish))
            return new AbilityLevelResult();

        var battleInfos = new List<HitMonsterInstance>();
        foreach (var entity in targetEntities)
        {
            var type = entity is EntityMonster { IsAlive: false }
                ? MonsterBattleType.DirectDieSkipBattle
                : MonsterBattleType.TriggerBattle;
            battleInfos.Add(new HitMonsterInstance(entity.EntityId, type));
        }

        var hasLiveMonster = targetEntities.OfType<EntityMonster>().Any(x => x.IsAlive);
        BattleInstance? instance = null;
        if (hasLiveMonster)
            instance = await Player.BattleManager!.StartBattle(attackEntity, targetEntities, req.SkillIndex == 1);

        return new AbilityLevelResult(instance, battleInfos);
    }

    private AdventureAbilityConfigListInfo? GetAbilityConfig(BaseGameEntity entity)
    {
        if (entity is EntityMonster monster)
            return GameData.AdventureAbilityConfigListData.GetValueOrDefault(monster.MonsterData.ID);

        if (entity is AvatarSceneInfo avatar)
            if (GameData.AvatarConfigData.TryGetValue(avatar.AvatarInfo.AvatarId, out var excel))
                return GameData.AdventureAbilityConfigListData.GetValueOrDefault(excel.AdventurePlayerID);

        return null;
    }

    // Cache of each avatar's buffs that stack themselves when a teammate uses a technique: avatarId -> [(buffId, max
    // level)]. Data-driven; only avatars with such a listener have entries (currently just March 7th, 122401).
    private static readonly ConcurrentDictionary<int, List<(int BuffId, int MaxLv)>> TechniqueListenerCache = new();

    // After a technique is used, adds one stack (capped) to every avatar on the field other than the caster that has
    // such a teammate-technique listener buff. Reuses the SceneBuff -> ApplyBuff -> battle path.
    private async ValueTask StackTeammateTechniqueBuffs(AvatarSceneInfo caster)
    {
        foreach (var owner in Player.SceneInstance!.AvatarInfo.Values)
        {
            if (owner.AvatarInfo.BaseAvatarId == caster.AvatarInfo.BaseAvatarId) continue;
            foreach (var (buffId, maxLv) in GetTeammateTechniqueListenerBuffs(owner.AvatarInfo.AvatarId))
            {
                var prev = owner.BuffList.Find(x => x.BuffId == buffId)?.BuffLevel ?? 0;
                var lv = Math.Min(prev + 1, maxLv);
                if (lv <= prev) continue;
                owner.BuffList.RemoveAll(x => x.BuffId == buffId);
                var buff = new SceneBuff(buffId, lv, owner.AvatarInfo.BaseAvatarId);
                buff.DynamicValues["#ADF_1"] = lv;
                buff.DynamicValues["#ADF_2"] = maxLv;
                owner.BuffList.Add(buff);
                await Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(owner, buff));
            }
        }
    }

    // Data-driven: a CharacterAbility maze buff of the avatar whose modifier adds that same buff again in
    // OnAfterLocalPlayerUseSkill is one that stacks itself on teammates' techniques.
    private static List<(int BuffId, int MaxLv)> GetTeammateTechniqueListenerBuffs(int avatarId)
    {
        return TechniqueListenerCache.GetOrAdd(avatarId, id =>
        {
            var result = new List<(int, int)>();
            var seen = new HashSet<int>();
            foreach (var (key, excel) in GameData.MazeBuffData)
            {
                if (key / 10 / 100 != id) continue;
                if (excel.MazeBuffType != "Character" || excel.InBattleBindingType != "CharacterAbility") continue;
                var buffId = key / 10;
                if (!seen.Add(buffId) || string.IsNullOrEmpty(excel.ModifierName)) continue;
                if (!GameData.AdventureModifierData.TryGetValue(excel.ModifierName, out var modifier)) continue;
                if (!TaskListAddsMazeBuff(modifier.OnAfterLocalPlayerUseSkill, buffId)) continue;
                result.Add((buffId, GameData.MazeBuffData.Values.Where(x => x.ID == buffId).Max(x => x.Lv)));
            }

            return result;
        });
    }

    // Recursively checks whether the task list, including the success/failure branches of a PredicateTaskList, contains
    // an AddMazeBuff for this buffId.
    private static bool TaskListAddsMazeBuff(List<TaskConfigInfo> tasks, int buffId)
    {
        foreach (var task in tasks)
        {
            if (task is AddMazeBuff add && add.ID == buffId) return true;
            if (task is PredicateTaskList pred &&
                (TaskListAddsMazeBuff(pred.SuccessTaskList, buffId) || TaskListAddsMazeBuff(pred.FailedTaskList, buffId)))
                return true;
        }

        return false;
    }
}

public record SkillResultData(
    Retcode RetCode,
    BattleInstance? Instance = null,
    List<HitMonsterInstance>? TriggerBattleInfos = null);
