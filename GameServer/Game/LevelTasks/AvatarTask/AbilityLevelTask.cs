using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using March7thHoney.Data;
using March7thHoney.Data.Config;
using March7thHoney.Data.Config.SummonUnit;
using March7thHoney.Data.Config.Task;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.RogueMagic;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Component;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.LevelTasks.AvatarTask;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
public class AbilityLevelTask(PlayerInstance player)
{
    public PlayerInstance Player { get; set; } = player;

    // buff ids this cast's ability delivers to enemies via attack-detect (SummonUnitTriggerAttackDetectConfig); the maze-buff backfill skips these so they aren't also applied to the caster
    public HashSet<int> SummonAttackDetectBuffs { get; } = [];

    #region Selector

    public async ValueTask<object> TargetAlias(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;
        var selector = param.TargetEvaluator!;
        var casterEntity = param.CasterEntity;
        var targetEntities = param.TargetEntities;

        if (selector is TargetAlias target)
            return target.Alias switch
            {
                "Caster" or "ModifierOwnerEntity" or "CasterSummoner" => [casterEntity],
                "ParamEntity" or "ParamEntityList" or "AllEnemy" or "AbilityTargetEntity" => targetEntities,
                "AdvTeamMembers" => Player.SceneInstance!.AvatarInfo.Values.ToList().OfType<BaseGameEntity>().ToList(),
                "AdvLocalPlayer" => Player.SceneInstance!.AvatarInfo.Values
                    .Where(x => x.AvatarInfo.BaseAvatarId == Player.LineupManager!.GetCurLineup()?.LeaderAvatarId)
                    .OfType<BaseGameEntity>().ToList(),
                _ => targetEntities
            };

        return new List<BaseGameEntity>();
    }

    #endregion

    #region Manage

    public async ValueTask<AbilityLevelResult> TriggerTasks(AdventureAbilityConfigListInfo abilities,
        List<TaskConfigInfo> tasks, BaseGameEntity casterEntity, List<BaseGameEntity> targetEntities,
        SceneCastSkillCsReq req,
        string? modifierName = null)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];
        foreach (var task in tasks)
            try
            {
                var res = await TriggerTask(new AbilityLevelParam(abilities, task, casterEntity, targetEntities, req,
                    modifierName));
                if (res.BattleInfos != null) battleInfos.AddRange(res.BattleInfos);

                if (res.Instance != null) instance = res.Instance;
            }
            catch (Exception e)
            {
                Logger.GetByClassName().Error("An error occured, ", e);
            }

        return new AbilityLevelResult(instance, battleInfos);
    }

    public async ValueTask<AbilityLevelResult> TriggerTask(AbilityLevelParam param)
    {
        try
        {
            var methodName = param.Act.Type.Replace("RPG.GameCore.", "");

            // try to get from cache
            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return new AbilityLevelResult();

            var res = method(param);
            var re = await res;
            if (re is AbilityLevelResult result) return result;
        }
        catch (Exception e)
        {
            Logger.GetByClassName().Error("An error occured, ", e);
        }

        return new AbilityLevelResult();
    }

    private ExecuteTask? GetOrCreateExecuteTask(string methodName)
    {
        // try to get from cache
        if (_cachedTasks.TryGetValue(methodName, out var method)) return method;
        var methodProp = GetType().GetMethod(methodName);
        if (methodProp == null) return null;

        method = (ExecuteTask)Delegate.CreateDelegate(typeof(ExecuteTask), this, methodProp);
        _cachedTasks[methodName] = method; // cached

        return method;
    }

    private delegate ValueTask<object> ExecuteTask(AbilityLevelParam param);

    private readonly ConcurrentDictionary<string, ExecuteTask> _cachedTasks = [];

    private async ValueTask<bool> EvaluatePredicate(PredicateConfigInfo predicate, AbilityLevelParam param)
    {
        if (predicate is UnknownPredicateConfigInfo || string.IsNullOrEmpty(predicate.Type)) return true;

        var methodName = predicate.Type.Replace("RPG.GameCore.", "");
        var method = GetOrCreateExecuteTask(methodName);
        if (method == null) return true;

        var resp = await method(param with { Act = predicate });
        if (resp is not bool result) return false;
        return predicate.Inverse ? !result : result;
    }

    #endregion

    #region Task

    public async ValueTask<object> PredicateTaskList(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is PredicateTaskList predicateTaskList)
        {
            // handle predicateCondition
            var methodName = predicateTaskList.Predicate.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            var res = true;
            if (method != null)
            {
                var resp = await method(param with { Act = predicateTaskList.Predicate });
                if (resp is not bool r)
                    res = false;
                else
                    res = predicateTaskList.Predicate.Inverse ? !r : r;
            }

            if (res)
                foreach (var task in predicateTaskList.SuccessTaskList)
                {
                    var result = await TriggerTask(param with { Act = task });
                    if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                    if (result.Instance != null) instance = result.Instance;
                }
            else
                foreach (var task in predicateTaskList.FailedTaskList)
                {
                    var result = await TriggerTask(param with { Act = task });
                    if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                    if (result.Instance != null) instance = result.Instance;
                }
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    // Random technique rewards on the official server (e.g. the Elation (欢愉) technique "燃起来了!"): the client sends
    // its roll result _result back in the cast request, and the server uses it to choose which maze buff to add.
    public ValueTask<object> AdvByCompareDynamicValue(AbilityLevelParam param)
    {
        if (param.Act is not Data.Config.Task.AdvByCompareDynamicValue cmp)
            return ValueTask.FromResult<object>(true);

        var dv = param.Request?.DynamicValues.FirstOrDefault(x => x.Key == cmp.DynamicKey);
        // When the request lacks the key, or the compare value is a dynamic expression (which this server does not
        // evaluate), keep the default of letting the predicate pass.
        if (dv == null || cmp.CompareValue.IsDynamic)
            return ValueTask.FromResult<object>(!cmp.Inverse);

        var left = dv.Value;
        var right = cmp.CompareValue.GetValue();
        var res = cmp.CompareType switch
        {
            "Equal" => Math.Abs(left - right) < 0.0001f,
            "NotEqual" => Math.Abs(left - right) >= 0.0001f,
            "Greater" => left > right,
            "GreaterEqual" => left >= right,
            "Less" => left < right,
            "LessEqual" => left <= right,
            _ => true
        };
        return ValueTask.FromResult<object>(res);
    }

    public async ValueTask<object> AdventureTriggerAttack(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is AdventureTriggerAttack adventureTriggerAttack)
        {
            // Resolve the hit targets: evaluate AttackTargetType when it is set, otherwise fall back to the hit
            // monsters the client reported in the request.
            List<BaseGameEntity> target;
            if (!string.IsNullOrEmpty(adventureTriggerAttack.AttackTargetType.Type))
            {
                var methodName = adventureTriggerAttack.AttackTargetType.Type.Replace("RPG.GameCore.", "");
                var method = GetOrCreateExecuteTask(methodName);
                if (method == null) return new AbilityLevelResult();
                var resp = await method(param with { TargetEvaluator = adventureTriggerAttack.AttackTargetType });
                if (resp is not List<BaseGameEntity> t) return new AbilityLevelResult();
                target = t;
            }
            else
            {
                target = [.. param.TargetEntities];
            }

            var detect = adventureTriggerAttack.SummonUnitTriggerAttackDetectConfig;
            EntitySummonUnit? detectSummon = null;
            UnitCustomTriggerConfigInfo? detectTrigger = null;
            if (detect != null && Player.SceneInstance != null)
            {
                detectSummon = Player.SceneInstance.SummonUnit.Values
                    .FirstOrDefault(x => x.SummonUnitId == (int)detect.SummonUnitID);
                detectTrigger = detectSummon?.TriggerList
                    .FirstOrDefault(x => x.TriggerName == detect.DetectSummonUnitTriggerName);

                if (target.Count == 0 && detectSummon?.Motion.Pos != null && detectTrigger != null)
                {
                    var center = detectSummon.Motion.Pos;
                    var radius = detectTrigger.Radius.GetValue() * 1000L;
                    if (radius > 0)
                        target = Player.SceneInstance.Entities.Values.OfType<EntityMonster>()
                            .Where(x => x.IsAlive && GetDistanceSquared(x.Position, center) <= radius * radius)
                            .Cast<BaseGameEntity>()
                            .ToList();
                }
            }

            foreach (var task in adventureTriggerAttack.OnAttack)
            {
                var result = await TriggerTask(param with { Act = task, TargetEntities = target });
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);
            }

            // Attack-detect field: deliver the maze buff of the summon unit's matching trigger to the attacked enemies
            // (e.g. Gilgamesh 150901, which the enemy carries into battle).
            if (detectSummon != null && detectTrigger != null && Player.SceneInstance != null)
            {
                // Record the buffs delivered through attack-detect so the backfill can exclude them; otherwise the same
                // buff would also be added to the caster (e.g. 150901 would freeze it).
                foreach (var task in detectTrigger.OnTriggerEnter)
                    if (task is AddMazeBuff addMazeBuff)
                        SummonAttackDetectBuffs.Add(addMazeBuff.ID);

                var detectIds = target.OfType<EntityMonster>().Where(x => x.IsAlive)
                    .Select(x => (uint)x.EntityId).ToList();
                if (detectIds.Count > 0)
                    await Player.SceneInstance.TriggerSummonUnit(detectSummon.EntityId,
                        detectTrigger.TriggerName, detectIds);
            }

            // Normal attacks also start battles on live hits. The official server gets these targets from
            // assistMonsterEntityIdList rather than the ability config.
            var hasHitMonster = target.OfType<EntityMonster>().Any(x => x.IsAlive);
            var shouldTriggerBattle = adventureTriggerAttack.TriggerBattle
                                      || !string.IsNullOrEmpty(adventureTriggerAttack.EnterBattleSelectTargetType)
                                      || (param.Request.SkillIndex == 0 && hasHitMonster);

            if (target.Count > 0 && shouldTriggerBattle)
            {
                foreach (var task in adventureTriggerAttack.OnBattle)
                {
                    var result = await TriggerTask(param with { Act = task, TargetEntities = target });
                    if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);
                }

                foreach (var entity in target)
                {
                    var type = MonsterBattleType.TriggerBattle;
                    if (entity is EntityMonster { IsAlive: false })
                        type = MonsterBattleType.DirectDieSkipBattle;

                    battleInfos.Add(new HitMonsterInstance(entity.EntityId, type));
                }

                instance = await Player.BattleManager!.StartBattle(param.CasterEntity, target,
                    param.Request.SkillIndex == 1);
            }
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    private static long GetDistanceSquared(Position position, Vector center)
    {
        var x = (long)position.X - center.X;
        var z = (long)position.Z - center.Z;
        return x * x + z * z;
    }

    public async ValueTask<object> AdventureTriggerTargetAbility(AbilityLevelParam param)
    {
        if (param.Act is not AdventureTriggerTargetAbility trigger || string.IsNullOrEmpty(trigger.AbilityName))
            return new AbilityLevelResult();

        var targetAbility = param.AdventureAbility.AbilityList.FirstOrDefault(x => x.Name == trigger.AbilityName);
        if (targetAbility == null) return new AbilityLevelResult();

        var activeAbilities = param.ActiveAbilities ?? new HashSet<string>(StringComparer.Ordinal);
        if (!activeAbilities.Add(targetAbility.Name)) return new AbilityLevelResult();

        try
        {
            BattleInstance? instance = null;
            List<HitMonsterInstance> battleInfos = [];
            foreach (var task in targetAbility.OnStart)
            {
                var result = await TriggerTask(param with { Act = task, ActiveAbilities = activeAbilities });
                if (result.Instance != null) instance = result.Instance;
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);
            }

            return new AbilityLevelResult(instance, battleInfos);
        }
        finally
        {
            activeAbilities.Remove(targetAbility.Name);
        }
    }

    // Tingyun's technique (skill 120207) immediately restores 50 energy to the caster; she is the only avatar that uses
    // AdventureModifyTeamPlayerSP. Engine SP is energy x100 with full energy normalized to 10000, so 50 energy = 5000.
    private const int TechniqueSpRecoverFallback = 5000;

    public async ValueTask<object> AdventureModifyTeamPlayerSP(AbilityLevelParam param)
    {
        if (param.Act is not Data.Config.Task.AdventureModifyTeamPlayerSP modifySp)
            return new AbilityLevelResult();

        // Resolve the targets (the Caster itself for Tingyun's technique); fall back to the caster when the evaluator
        // is missing.
        List<BaseGameEntity> targets;
        var methodName = modifySp.SpecifyTargetType.Type.Replace("RPG.GameCore.", "");
        var method = GetOrCreateExecuteTask(methodName);
        if (method != null &&
            await method(param with { TargetEvaluator = modifySp.SpecifyTargetType }) is List<BaseGameEntity> resolved &&
            resolved.Count > 0)
            targets = resolved;
        else
            targets = [param.CasterEntity];

        // The value from the client data is a dynamic expression (PostfixExpr), and this server has no expression
        // evaluator. A non-dynamic value is used directly; otherwise fall back to the fixed recovery of Tingyun's
        // technique.
        var recover = modifySp.ModifyValue.GetValue() * 100;
        if (recover <= 0) recover = TechniqueSpRecoverFallback;

        var lineup = Player.LineupManager!.GetCurLineup();
        var isExtra = lineup?.IsExtraLineup() ?? false;

        var changed = false;
        foreach (var avatar in targets.OfType<AvatarSceneInfo>())
        {
            var info = avatar.AvatarInfo;
            info.SetCurSp(Math.Min(info.GetCurSp(isExtra) + recover, 10000), isExtra);
            changed = true;
        }

        // Sync the lineup's energy so the overworld UI updates immediately; the next battle picks up the updated SP
        // through ToBattleProto.
        if (changed && lineup != null)
            await Player.SendPacket(new PacketSyncLineupNotify(lineup));

        return new AbilityLevelResult();
    }

    public async ValueTask<object> AddMazeBuff(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is AddMazeBuff addMazeBuff)
        {
            var methodName = addMazeBuff.TargetType.Type.Replace("RPG.GameCore.", "");
            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return new AbilityLevelResult();
            var resp = await method(param with { TargetEvaluator = addMazeBuff.TargetType });

            Dictionary<string, float> dynamic = [];
            foreach (var dynamicValue in addMazeBuff.DynamicValues)
                dynamic.Add(dynamicValue.Key, dynamicValue.Value.GetValue());

            if (resp is not List<BaseGameEntity> target) return new AbilityLevelResult(instance, battleInfos);

            var time = addMazeBuff.LifeTime.FixedValue.Value < -1 ? 20 :
                addMazeBuff.LifeTime.FixedValue.Value is > 30 or < 10 ? -1 : addMazeBuff.LifeTime.FixedValue.Value;

            foreach (var entity in target)
            {
                if (!await EvaluatePredicate(addMazeBuff.Condition, param with { TargetEntities = [entity] }))
                    continue;

                var buffOwnerAvatarId = (param.CasterEntity as AvatarSceneInfo)?.AvatarInfo.BaseAvatarId ?? 0;
                var summonCaster = param.CasterEntity as EntitySummonUnit;
                if (summonCaster != null) buffOwnerAvatarId = summonCaster.CreateAvatarId;

                await entity.AddBuff(new SceneBuff(addMazeBuff.ID, 1, buffOwnerAvatarId, time)
                {
                    DynamicValues = dynamic,
                    // Buff source is cast request entity when available, otherwise current caster entity.
                    CastEntityId = param.Request.CastEntityId == 0
                        ? param.CasterEntity.EntityId
                        : (int)param.Request.CastEntityId,
                    // For summon-unit driven buffs, official uses the summon's own entity id as summon reference.
                    SummonUnitEntityId = summonCaster?.EntityId ?? 0
                });
            }
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    public async ValueTask<object> RemoveMazeBuff(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is RemoveMazeBuff removeMazeBuff)
        {
            var methodName = removeMazeBuff.TargetType.Type.Replace("RPG.GameCore.", "");
            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return new AbilityLevelResult();
            var resp = await method(param with { TargetEvaluator = removeMazeBuff.TargetType });

            if (resp is not List<BaseGameEntity> target) return new AbilityLevelResult(instance, battleInfos);

            foreach (var entity in target.OfType<AvatarSceneInfo>())
                await entity.RemoveBuff(removeMazeBuff.ID);
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    public async ValueTask<object> AdventureFireProjectile(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is AdventureFireProjectile adventureFireProjectile)
        {
            foreach (var task in adventureFireProjectile.OnProjectileHit)
            {
                var result = await TriggerTask(param with { Act = task });
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                if (result.Instance != null)
                    instance = result.Instance;
            }

            foreach (var task in adventureFireProjectile.OnProjectileLifetimeFinish)
            {
                var result = await TriggerTask(param with { Act = task });
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                if (result.Instance != null)
                    instance = result.Instance;
            }
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    public async ValueTask<object> NewAdventureFireProjectile(AbilityLevelParam param)
    {
        BattleInstance? instance = null;
        List<HitMonsterInstance> battleInfos = [];

        if (param.Act is AdventureFireProjectile adventureFireProjectile)
        {
            foreach (var task in adventureFireProjectile.OnProjectileHit)
            {
                var result = await TriggerTask(param with { Act = task });
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                if (result.Instance != null)
                    instance = result.Instance;
            }

            foreach (var task in adventureFireProjectile.OnProjectileLifetimeFinish)
            {
                var result = await TriggerTask(param with { Act = task });
                if (result.BattleInfos != null) battleInfos.AddRange(result.BattleInfos);

                if (result.Instance != null)
                    instance = result.Instance;
            }
        }

        return new AbilityLevelResult(instance, battleInfos);
    }

    public async ValueTask<object> CreateSummonUnit(AbilityLevelParam param)
    {
        if (param.Act is CreateSummonUnit createSummonUnit)
        {
            if (!GameData.SummonUnitDataData.TryGetValue(createSummonUnit.SummonUnitID, out var excel))
                return new AbilityLevelResult();

            var fallbackMotion = new MotionInfo
            {
                Pos = Player.Data.Pos?.ToProto() ?? new Vector(),
                Rot = Player.Data.Rot?.ToProto() ?? new Vector()
            };
            var spawnMotion = param.Request.TargetMotion ?? fallbackMotion;
            if (spawnMotion.Pos == null) spawnMotion.Pos = fallbackMotion.Pos;
            if (spawnMotion.Rot == null) spawnMotion.Rot = fallbackMotion.Rot;

            const int summonLifeTimeMs = 45 * 1000;

            // Official FollowField summons tied to timed maze buffs use LifeTimeMs = -1 (e.g. Cyrene 14151).
            // Buff expiry removes the modifier and destroys the summon. A fixed timer could cut short
            // a refreshed buff and its effects on enemies. Keep the 45s fallback for unowned or permanent buffs.
            var lifeTimeMs = param.CasterEntity is AvatarSceneInfo avatarCaster &&
                             IsDestroyedByTimedMazeBuff(avatarCaster, excel.ID)
                ? -1
                : summonLifeTimeMs;

            // Field-type summon units attach to the caster; only then does the client run field detection and report
            // RefreshTrigger. Attack-detect units are excluded: they go through server-side attack detection, and
            // attaching them would let the client's OnTriggerExit remove their buff by mistake (e.g. Gilgamesh 150901).
            var attachPoint = excel.ConfigInfo?.AttachPoint;
            var attachToCaster = attachPoint == "Origin"
                                 || (string.IsNullOrEmpty(attachPoint) && !IsAttackDetectSummon(param.AdventureAbility, excel.ID));

            var unit = new EntitySummonUnit
            {
                EntityId = 0,
                CreateAvatarEntityId = param.CasterEntity.EntityId,
                AttachEntityId = attachToCaster ? param.CasterEntity.EntityId : 0,
                SummonUnitId = excel.ID,
                CreateAvatarId = (param.CasterEntity as AvatarSceneInfo)?.AvatarInfo.BaseAvatarId ?? 0,
                LifeTimeMs = lifeTimeMs,
                TriggerList = excel.ConfigInfo?.TriggerConfig.CustomTriggers ?? [],
                Motion = spawnMotion
            };

            await Player.SceneInstance!.AddSummonUnitEntity(unit);

            // The unit's own OnCreate tasks (e.g. Silver Wolf LV.999 technique summon 15061 adds buff 150601).
            if (excel.ConfigInfo?.OnCreate is { Count: > 0 } onCreate)
                await TriggerTasks(param.AdventureAbility, onCreate, param.CasterEntity, [param.CasterEntity],
                    param.Request);
        }

        return new AbilityLevelResult();
    }

    // Whether an ability of the avatar uses the summon unit for attack detection (SummonUnitTriggerAttackDetectConfig).
    // Such units go through server-side attack detection and are not attached to the caster, so that an OnTriggerExit
    // from the client's field trigger cannot remove their buff by mistake.
    private static bool IsAttackDetectSummon(AdventureAbilityConfigListInfo abilities, int summonUnitId)
    {
        return abilities.AbilityList.Any(a =>
            ContainsAttackDetect(a.OnStart, summonUnitId) || ContainsAttackDetect(a.OnAdd, summonUnitId)
            || ContainsAttackDetect(a.OnRemove, summonUnitId) || ContainsAttackDetect(a.OnAbort, summonUnitId));
    }

    private static bool ContainsAttackDetect(List<TaskConfigInfo> tasks, int summonUnitId)
    {
        foreach (var t in tasks)
        {
            if (t is AdventureTriggerAttack { SummonUnitTriggerAttackDetectConfig: { } d } && d.SummonUnitID == (uint)summonUnitId)
                return true;
            if (t is PredicateTaskList p
                && (ContainsAttackDetect(p.SuccessTaskList, summonUnitId) || ContainsAttackDetect(p.FailedTaskList, summonUnitId)))
                return true;
        }

        return false;
    }

    // Whether the caster holds an unexpired maze buff with a finite duration whose modifier destroys this summon unit
    // in OnDestroy.
    private static bool IsDestroyedByTimedMazeBuff(AvatarSceneInfo caster, int summonUnitId)
    {
        foreach (var buff in caster.BuffList)
        {
            if (buff.Duration < 0 || buff.IsExpired()) continue;
            if (!GameData.MazeBuffData.TryGetValue(buff.BuffId * 10 + buff.BuffLevel, out var buffExcel)) continue;
            if (!caster.TryResolveModifier(buffExcel.ModifierName, out _, out var modifier)) continue;
            if (ContainsDestroySummon(modifier.OnDestroy, summonUnitId)) return true;
        }

        return false;
    }

    private static bool ContainsDestroySummon(List<TaskConfigInfo> tasks, int summonUnitId)
    {
        foreach (var t in tasks)
        {
            if (t is DestroySummonUnit destroy && destroy.SummonUnit.SummonUnitID == summonUnitId)
                return true;
            if (t is PredicateTaskList p
                && (ContainsDestroySummon(p.SuccessTaskList, summonUnitId) ||
                    ContainsDestroySummon(p.FailedTaskList, summonUnitId)))
                return true;
        }

        return false;
    }

    public async ValueTask<object> DestroySummonUnit(AbilityLevelParam param)
    {
        if (param.Act is DestroySummonUnit destroySummonUnit)
        {
            var summonUnitId = destroySummonUnit.SummonUnit.SummonUnitID;
            var unit = Player.SceneInstance!.Entities.Values.OfType<EntitySummonUnit>()
                .FirstOrDefault(entity => entity.SummonUnitId == summonUnitId);
            var summoner = unit != null &&
                           Player.SceneInstance.Entities.TryGetValue(unit.CreateAvatarEntityId, out var owner)
                ? owner
                : param.CasterEntity;

            await Player.SceneInstance.RemoveSummonUnitById(summonUnitId); // TODO

            // The unit's OnDestroy runs against its summoner. Removing first keeps an OnDestroy that names the
            // same unit from recursing.
            if (unit != null && GameData.SummonUnitDataData.TryGetValue(summonUnitId, out var excel) &&
                excel.ConfigInfo?.OnDestroy is { Count: > 0 } onDestroy)
                await TriggerTasks(param.AdventureAbility, onDestroy, summoner, [summoner], param.Request);
        }

        return new AbilityLevelResult();
    }

    public async ValueTask<object> AddAdventureModifier(AbilityLevelParam param)
    {
        if (param.Act is AddAdventureModifier addAdventureModifier)
        {
            GameData.AdventureModifierData.TryGetValue(addAdventureModifier.ModifierName, out var modifier);
            if (modifier == null) return new AbilityLevelResult();

            if (param.CasterEntity is IGameModifier mod) await mod.AddModifier(addAdventureModifier.ModifierName);
        }

        return new AbilityLevelResult();
    }

    public async ValueTask<object> RemoveAdventureModifier(AbilityLevelParam param)
    {
        if (param.Act is RemoveAdventureModifier removeAdventureModifier)
        {
            GameData.AdventureModifierData.TryGetValue(removeAdventureModifier.ModifierName, out var modifier);
            if (modifier == null) return new AbilityLevelResult();

            if (param.CasterEntity is IGameModifier mod) await mod.RemoveModifier(removeAdventureModifier.ModifierName);
        }

        return new AbilityLevelResult();
    }

    public async ValueTask<object> RemoveSelfModifier(AbilityLevelParam param)
    {
        if (param.ModifierName != null)
            if (param.CasterEntity is IGameModifier mod)
                await mod.RemoveModifier(param.ModifierName);

        return new AbilityLevelResult();
    }

    public async ValueTask<object> RefreshMazeBuffTime(AbilityLevelParam param)
    {
        if (param.Act is RefreshMazeBuffTime refreshMazeBuffTime)
        {
            // get buff
            var buff = param.CasterEntity.BuffList.FirstOrDefault(x => x.BuffId == refreshMazeBuffTime.ID);
            if (buff == null) return new AbilityLevelResult();
            buff.Duration = refreshMazeBuffTime.LifeTime.GetValue();
            await Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(param.CasterEntity, buff));
        }

        return new AbilityLevelResult();
    }

    public async ValueTask<object> AdvModifyMaxMazeMP(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;
        // The lineup recalculation is authoritative, which keeps this consistent with the ToProto / packet-sending
        // path.
        if (param.Act is AdvModifyMaxMazeMP)
            Player.LineupManager!.RecalculateExtraMpCount();
        return new AbilityLevelResult();
    }

    public async ValueTask<object> AdventureSetAttackTargetMonsterDie(AbilityLevelParam param)
    {
        var avatar = param.CasterEntity as AvatarSceneInfo;
        if (GameData.AvatarConfigData.TryGetValue(avatar?.AvatarInfo.AvatarId ?? 0, out var excel))
        {
            var adventurePlayerExcel = GameData.AdventurePlayerData.GetValueOrDefault(excel.AdventurePlayerID);
            if (adventurePlayerExcel != null && adventurePlayerExcel.MazeSkillIdList.Count > param.Request.SkillIndex)
            {
                var skill = GameData.MazeSkillData.GetValueOrDefault(
                    adventurePlayerExcel.MazeSkillIdList[(int)param.Request.SkillIndex]);

                await Player.LineupManager!.CostMp(skill?.MPCost ?? 1, param.Request.CastEntityId);
            }
        }

        foreach (var targetEntity in param.TargetEntities)
        {
            if (targetEntity is not EntityMonster monster) continue;

            // A Divergent Universe room is cleared, paid and unlocked by its BATTLE result: its mobs survive the
            // rank-kill and roll into the fight like an Elite does.
            if (monster.MonsterData.Rank < MonsterRankEnum.Elite &&
                monster.Scene.GameModeType != GameModeTypeEnum.TournRogue)
            {
                await monster.Kill();

                // 4.2 proto migration in progress: rogue extension rewards are temporarily disabled here.
            }
        }

        return new AbilityLevelResult();
    }

    #endregion

    #region Predicate

    public async ValueTask<object> AdvByContainBehaviorFlag(AbilityLevelParam param)
    {
        if (param.Act is not Data.Config.Task.AdvByContainBehaviorFlag predicate ||
            string.IsNullOrEmpty(predicate.Flag))
            return false;

        List<BaseGameEntity> targets = param.TargetEntities;
        if (!string.IsNullOrEmpty(predicate.TargetType.Type))
        {
            var methodName = predicate.TargetType.Type.Replace("RPG.GameCore.", "");
            var method = GetOrCreateExecuteTask(methodName);
            if (method == null ||
                await method(param with { TargetEvaluator = predicate.TargetType }) is not List<BaseGameEntity> resolved)
                return false;

            targets = resolved;
        }

        return targets.OfType<IGameModifier>().Any(entity => entity.Modifiers.Any(modifierName =>
            GameData.AdventureModifierData.TryGetValue(modifierName, out var modifier) &&
            modifier.BehaviorFlagList.Contains(predicate.Flag, StringComparer.Ordinal)));
    }

    public async ValueTask<object> ByAllowInstantKill(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        foreach (var targetEntity in param.TargetEntities)
            if (targetEntity is EntityMonster monster)
                if (monster.MonsterData.Rank < MonsterRankEnum.Elite)
                    return true;

        return false;
    }

    public async ValueTask<object> ByIsContainAdventureModifier(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        if (param.Act is ByIsContainAdventureModifier byIsContain)
        {
            // get target
            var result = false;
            var methodName = byIsContain.TargetType.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return false;

            var resp = await method(param with { TargetEvaluator = byIsContain.TargetType });

            if (resp is List<BaseGameEntity> target)
                foreach (var entity in target)
                {
                    if (entity is not IGameModifier modifier) continue;
                    if (modifier.Modifiers.Contains(byIsContain.ModifierName))
                    {
                        result = true;
                        break;
                    }
                }

            return result;
        }

        return false;
    }

    public async ValueTask<object> AdventureByInMotionState(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        return true;
    }

    public async ValueTask<object> AdventureByPlayerCurrentSkillType(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        if (param.Act is AdventureByPlayerCurrentSkillType byPlayerCurrentSkillType)
            return param.Request.SkillIndex == (uint)byPlayerCurrentSkillType.SkillType;

        return false;
    }

    public async ValueTask<object> ByCompareCarryMazebuff(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        if (param.Act is ByCompareCarryMazebuff byCompareCarryMazebuff)
            return param.CasterEntity.BuffList.Any(x => x.BuffId == byCompareCarryMazebuff.BuffID);

        return false;
    }

    public async ValueTask<object> ByAnd(AbilityLevelParam param)
    {
        await ValueTask.CompletedTask;

        if (param.Act is ByAnd byAnd)
        {
            foreach (var task in byAnd.PredicateList)
            {
                var methodName = task.Type.Replace("RPG.GameCore.", "");

                var method = GetOrCreateExecuteTask(methodName);
                if (method == null) return false;

                var resp = await method(param with { Act = task });
                if (resp is not bool res) return false;
                res = task.Inverse ? !res : res;
                if (!res) return false;
            }

            return true;
        }

        return false;
    }

    #endregion
}

public record AbilityLevelResult(BattleInstance? Instance = null, List<HitMonsterInstance>? BattleInfos = null);

public record AbilityLevelParam(
    AdventureAbilityConfigListInfo AdventureAbility,
    TaskConfigInfo Act,
    BaseGameEntity CasterEntity,
    List<BaseGameEntity> TargetEntities,
    SceneCastSkillCsReq Request,
    string? ModifierName,
    TargetEvaluator? TargetEvaluator = null,
    HashSet<string>? ActiveAbilities = null);
