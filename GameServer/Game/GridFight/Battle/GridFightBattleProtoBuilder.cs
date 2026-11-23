using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Avatar;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Lineup;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight.Battle;

public static class GridFightBattleProtoBuilder
{
    private const int GridFightBaselineBuffId = 35509001;
    private const int FinalBattleTurnHealingBuffId = 35602011;
    private const int RewardBattleTalentBuffId = 35602061;
    private const int GridFightCombatTalentBuffId = 35604031;

    // Override stale or missing 4.4 RuleParamList IDs in Resource.bin with 4.5 battle MazeBuff IDs.
    // Official captures confirm used mappings; remaining 200x/402x IDs follow adjacent numbering.
    private static readonly IReadOnlyDictionary<uint, int> GridFight45AffixBuffs =
        new Dictionary<uint, int>
        {
            [1001] = 35301001,
            [1002] = 35301002,
            [1003] = 35301003,
            [1004] = 35301004,
            [1005] = 35301005,
            [2002] = 35302002,
            [2003] = 35302003,
            [2004] = 35302004,
            [2005] = 35302005,
            [2006] = 35302006,
            [3002] = 35302001,
            [3003] = 35303003,
            [3006] = 35302007,
            [3007] = 35302009,
            [4001] = 35303006,
            [4002] = 35303001,
            [4003] = 35303013,
            [4006] = 35303016,
            [4010] = 35303011,
            [4011] = 35303012,
            [4012] = 35303002,
            [4016] = 35303028,
            [4017] = 35303029,
            [4021] = 35303031,
            [4022] = 35303032,
            [4023] = 35303033,
            [4024] = 35303034,
            [4025] = 35303035,
            [4026] = 35303036,
            [4027] = 35303037,
            [4028] = 35303038,
            [40140] = 35303020,
        };
    // In the supplied 4.5 captures SkillIndex is stable for the actual team-leader role.
    // These are the roles observed using technique-style entry (SkillIndex=2). Unobserved roles
    // retain the resource-based fallback below instead of being guessed.
    private static readonly HashSet<uint> GridFightSkillIndex2RoleIds =
        [1005, 1009, 1317, 1402, 1505];
    private static readonly HashSet<uint> GridFightSkillIndex1RoleIds =
        [1013, 1102, 1213, 1404, 1502, 1504, 15061, 15062, 15063];

    private static readonly (int Id, int CurSp, int MaxSp)[] OfficialBattleEvents =
    [
        (11216, 0, 100000),
        (60014, 0, 100000),
        (60020, 70000, 100000),
        (60023, 0, 100000),
        (60024, 0, 200000),
        (60028, 100000, 100000),
        (60036, 0, 40000),
        (60045, 60000, 60000),
        (60047, 0, 100000),
    ];

    public static List<AvatarLineupData> Populate(
        BattleInstance battle,
        GridFightSession session,
        GridFightResourceCatalog catalog,
        SceneBattleInfo proto)
    {
        var collection = new PlayerDataCollection(
            battle.Player.Data,
            battle.Player.InventoryManager!.Data,
            battle.Lineup);
        var avatars = BuildBattleAvatars(battle, session);
        var foregroundRoles = session.Roles
            .Where(role => role.Position is >= 1 and <= 4)
            .OrderBy(role => role.Position)
            .ToList();
        var battleRoles = new List<GridFightRoleState>(foregroundRoles.Count);
        foreach (var role in foregroundRoles)
        {
            var avatar = ResolveBattleAvatar(battle, role, battle.Player.Data.CurrentGender, isForeground: true);
            if (avatar == null) continue;
            avatar.AvatarInfo.SetCurHp(10000, true);
            proto.BattleAvatarList.Add(BuildBattleAvatarProto(avatar, collection));
            battleRoles.Add(role);
        }
        AddEntryBuffs(battle, battleRoles);
        AddGridFightTeamLeaderBuff(battle);
        AddAffixBattleBuffs(battle, session);
        session.ApplyDotContractBattleBuff(battle);
        session.ApplyFateDivinerBattleBuffs(battle);
        session.ApplyAstaDogBattleBuff(battle);
        AddOfficialGridFightCombatBuffs(battle, session, catalog);

        proto.MonsterWaveList.Clear();
        foreach (var wavePlan in session.BattlePlan?.Waves ?? [])
        {
            var wave = new SceneMonsterWave
            {
                // Official GridFight battle packets keep the stage id only on SceneBattleInfo.
                // Setting the wave-level BattleStageId changes the packet shape and can make
                // the client resolve the wave through the normal stage pipeline instead.
                BattleWaveId = wavePlan.WaveId,
                MonsterParam = new SceneMonsterWaveParam
                {
                    MonsterWorldLevel = wavePlan.FormationWaveId,
                    EliteGroup = session.BattlePlan!.EliteGroupId,
                    SupplementalEliteGroup = wavePlan.FormationWaveId == 3 ? 846u : 0,
                },
            };
            wave.MonsterList.Add(wavePlan.Monsters.Select(monster =>
            {
                var battleInfo = new GridFightMonsterBattleInfo { RoleStar = monster.RoleStar };
                battleInfo.DropList.Add(monster.Drops
                    .Where(drop => drop.Type == GridFightBonusTypeEnum.Orb)
                    .Select(drop => new GridFightDropEntry
                    {
                        DropType = GridFightDropType.Orb,
                        ItemId = drop.ItemId,
                        Num = drop.Count,
                    }));
                return new SceneMonster
                {
                    MonsterId = monster.Monster.MonsterID,
                    ExtraInfo = new GridFightSceneMonsterExtraInfo { GridFightInfo = battleInfo },
                };
            }));
            proto.MonsterWaveList.Add(wave);
        }

        var activeRoles = session.Roles
            .Where(role => role.Position is >= 1 and <= 13)
            .OrderBy(role => role.Position)
            .ToList();
        session.RefreshRoleSavedValues();
        var gridFightInfo = new GridFightInfo
        {
            // Official 4.5 packets leave GridFightInfo.BattleWaveId at its protobuf default (0).
            // SceneMonsterWave.BattleWaveId is the per-wave field; forcing this outer field to 1
            // changes the GridFight battle-runtime packet shape.
            GridFightLineupHp = session.LineupHp,
            // Official 4.5 crown capture keeps this field at the pre-crown level cap while
            // ActiveRoleCount may be one or more higher. Capacity UI/validation uses MaxActiveRoles.
            MaxActiveRoleCount = Math.Min(GridFightSession.TeamPopulationLimit, session.BattleMaxActiveRoles + session.LizhiActiveRoleBonus),
            PenaltyBonusRuleId = session.CurrentPenaltyRuleId,
            EnemyDifficultyLevel = session.EffectiveEnemyDifficultyLevel,
            Season = session.Season,
            // Battle-only division supplies HP/ATK base multipliers; lobby rank and rewards remain S2.
            DivisionId = catalog.GetBattleDivisionId(session.DivisionId),
            ActiveRoleCount = (uint)activeRoles.Count + (session.PepeNpc == null ? 0u : 1u) +
                              (session.AstaDogNpc == null ? 0u : 1u) + (uint)session.LizhiNpcs.Count,
            IsOverlock = session.IsOverlock,
            NodeInfo = new GridFightNodeInfo
            {
                RouteId = catalog.StandardRouteId,
                ChapterId = session.ChapterId,
                SectionId = session.SectionId,
                NodeType = (uint)session.CurrentNode.NodeType,
            },
        };
        if (session.HackConsole != null)
            gridFightInfo.EnemyHpPercent = session.HackConsole.EnemyHpPercent;
        gridFightInfo.GridFightPortalBuffList.Add(session.ActivePortalIds.Select(portalId =>
            new GridFightPortalInfo
            {
                PortalBuffId = portalId,
            }));
        gridFightInfo.SyncAugmentInfo.Add(session.ActiveInvestmentIds.Select(augmentId =>
            new GridFightAugmentInfo { AugmentId = augmentId }));
        if (session.PepeNpc != null)
        {
            var npc = new GridFightNPCInfo
            {
                UniqueId = session.PepeNpc.UniqueId,
                NpcId = session.PepeNpc.NpcId,
                Pos = session.PepeNpc.Position,
            };
            npc.GridFightEquipmentList.Add(session.GetPepeEquippedEquipment().Select(equipment =>
                new BattleGridFightEquipInfo
                {
                    GridFightEquipmentId = equipment.EquipmentId,
                    UniqueId = equipment.UniqueId,
                }));
            gridFightInfo.GridGameNpcList.Add(npc);
        }
        if (session.AstaDogNpc != null)
        {
            var dog = new GridFightNPCInfo
            {
                UniqueId = session.AstaDogNpc.UniqueId,
                NpcId = session.AstaDogNpc.NpcId,
                Pos = session.AstaDogNpc.Position,
            };
            dog.GridFightEquipmentList.Add(session.GetAstaDogEquipment().Select(equipment =>
                new BattleGridFightEquipInfo
                {
                    GridFightEquipmentId = equipment.EquipmentId,
                    UniqueId = equipment.UniqueId,
                }));
            gridFightInfo.GridGameNpcList.Add(dog);
        }
        foreach (var lizhiNpc in session.LizhiNpcs)
        {
            var npc = new GridFightNPCInfo
            {
                UniqueId = lizhiNpc.UniqueId,
                NpcId = lizhiNpc.NpcId,
                Pos = lizhiNpc.Position,
            };
            npc.GridFightEquipmentList.Add(lizhiNpc.EquippedEquipmentUniqueIds
                .Select(uid => session.Equipments.FirstOrDefault(item => item.UniqueId == uid))
                .Where(item => item != null)
                .Select(item => new BattleGridFightEquipInfo
                {
                    GridFightEquipmentId = item!.EquipmentId,
                    UniqueId = item.UniqueId,
                }));
            gridFightInfo.GridGameNpcList.Add(npc);
        }
        foreach (var role in activeRoles.Where(role => role.Position is >= 5 and <= 13))
        {
            var avatar = ResolveBattleAvatar(battle, role, battle.Player.Data.CurrentGender, isForeground: false);
            if (avatar == null) continue;
            avatar.AvatarInfo.SetCurHp(10000, true);
            gridFightInfo.BackgroundAvatarList.Add(BuildBattleAvatarProto(avatar, collection));
        }
        foreach (var role in activeRoles)
            gridFightInfo.GridGameRoleList.Add(BuildRoleInfo(session, role, battle.Player.Data.CurrentGender));
        foreach (var (key, value) in session.PortalSavedValues)
            gridFightInfo.RoleSavedValues[key] = value;
        foreach (var (key, value) in session.RoleSavedValues)
            gridFightInfo.RoleSavedValues[key] = value;
        var roleTraitMembers = activeRoles
            .SelectMany(role => session.ResolveRoleTraitIds(role)
                .Select(traitId => (TraitId: traitId, Role: role)))
            .ToList();
        var pepeTraitMembers = session.ResolvePepeTraitMembers();
        foreach (var traitId in roleTraitMembers.Select(entry => entry.TraitId)
                     .Concat(pepeTraitMembers.Select(entry => entry.TraitId))
                     .Distinct()
                     .Order())
        {
            var members = roleTraitMembers
                .Where(entry => entry.TraitId == traitId)
                .Select(entry => entry.Role)
                .Distinct()
                .ToList();
            var hasPepeMember = session.PepeNpc != null && pepeTraitMembers.Any(entry => entry.TraitId == traitId);
            var count = (uint)members.Count +
                        (hasPepeMember ? 1u : 0u) +
                        session.ResolveTraitExtraMemberCount(traitId, activeRoles);
            var activeLayer = ResolveTraitLayer(traitId, count);
            var traitInfo = new GridFightTraitInfo
            {
                TraitId = traitId,
                Layer = activeLayer,
            };
            traitInfo.MemberList.Add(members.Select(role => new GridFightTraitMemberInfo
            {
                MemberRoleUniqueId = role.UniqueId,
                MemberType = GridFightTraitMemberType.Role,
                MemberRoleId = role.RoleId,
                UpdateSource = GameData.GridFightRoleBasicInfoData[role.RoleId].TraitList.Contains(traitId)
                    ? GridFightUpdateSource.Role
                    : GridFightUpdateSource.Equipment,
            }));
            if (hasPepeMember)
                traitInfo.MemberList.Add(new GridFightTraitMemberInfo
                {
                    MemberRoleUniqueId = session.PepeNpc!.UniqueId,
                    MemberRoleId = session.PepeNpc.NpcId,
                    MemberType = GridFightTraitMemberType.Reserved2,
                    UpdateSource = GridFightUpdateSource.Equipment,
                });
            if (activeLayer > 0 && !GridFightTraitCompatibility.UsesLayerOnlyWireShape(traitId))
            {
                PopulateTraitEffects(traitInfo);
                foreach (var effect in traitInfo.TraitEffectList)
                    GridFightManager.FillSeasonTwoBattleTraitEffect(session, effect);
                if (traitId == GridFightSession.FateDivinerTraitId &&
                    traitInfo.TraitEffectList.All(effect => effect.EffectId != GridFightSession.FateDivinerEffectId))
                    traitInfo.TraitEffectList.Add(new BattleGridFightTraitEffectInfo
                    {
                        EffectId = GridFightSession.FateDivinerEffectId,
                    });
            }
            gridFightInfo.GridFightTraitInfo.Add(traitInfo);
        }
        // Use the official fixed battle controllers. Sending resource-side 62xxx role/trait BEIDs
        // omits the trait controller and reduces damage for effects such as DoT (2008).
        battle.BattleEvents.Clear();
        foreach (var (id, curSp, maxSp) in OfficialBattleEvents)
            battle.BattleEvents.Add(id, new BattleEventInstance(id, curSp, maxSp));
        if (session.IsSeasonTwoRules)
        {
            // GridFightInfo field 23 carries the run's MoonGame (幻月游戏) sub-trait (same field name as the lobby
            // context value).
            gridFightInfo.NJBNOFIEDDE = new KJFEHJMAIAA { FMNPBOJGLFI = session.MoonGameSubTraitId };
            // MazeBuffData is keyed by ID * 10 + Lv. A buff the client has no MazeBuff row for blacks out the battle
            // load, so a train good is only sent once its row exists in the resource data.
            foreach (var buffId in session.GetTrailblazeBattleBuffIds()
                         .Where(buffId => GameData.MazeBuffData.ContainsKey(buffId * 10 + 1)))
                AddGlobalBuffIfMissing(battle, buffId);
            foreach (var buff in session.GetSeasonTwoBattleEffectBuffs())
                if (battle.Buffs.All(existing => existing.BuffID != buff.BuffID))
                    battle.Buffs.Add(buff);
        }
        proto.BattleGridFightData = gridFightInfo;
        return avatars;
    }

    private static uint ResolveRoleBattleMode(GridFightSession session, GridFightRoleState role)
    {
        var roleTraitIds = session.ResolveRoleTraitIds(role).Distinct().ToList();
        if (roleTraitIds.Count == 0) return 0;

        var activeRoles = session.Roles
            .Where(candidate => candidate.Position is >= 1 and <= 13)
            .ToList();
        var pepeTraits = session.ResolvePepeTraitMembers()
            .Select(entry => entry.TraitId)
            .ToHashSet();

        uint inactiveTraitCount = 0;
        foreach (var traitId in roleTraitIds)
        {
            var memberCount = (uint)activeRoles.Count(candidate =>
                                  session.ResolveRoleTraitIds(candidate).Contains(traitId)) +
                              (pepeTraits.Contains(traitId) ? 1u : 0u) +
                              session.ResolveTraitExtraMemberCount(traitId, activeRoles);
            if (ResolveTraitLayer(traitId, memberCount) == 0) inactiveTraitCount++;
        }

        return inactiveTraitCount;
    }

    private static void AddAffixBattleBuffs(BattleInstance battle, GridFightSession session)
    {
        foreach (var affixId in session.AffixIds.Distinct())
            foreach (var buffId in ResolveAffixBattleBuffIds(session, affixId))
                AddGlobalBuffIfMissing(battle, buffId);
    }

    private static IEnumerable<int> ResolveAffixBattleBuffIds(GridFightSession session, uint affixId)
    {
        // These affixes are conditional in the official captures and must not be emitted globally.
        switch (affixId)
        {
            case 1004:
                if (session.ChapterId >= 2) yield return 35301004;
                yield break;
            case 1005:
                if (session.ChapterId >= 3) yield return 35301005;
                yield break;
            case 3004:
                if (session.CurrentNode.NodeType == GridFightNodeTypeEnum.EliteBranch)
                    yield return 35303004;
                else if (session.CurrentNode.NodeType == GridFightNodeTypeEnum.Boss)
                    yield return 35303005;
                yield break;
            case 4013:
                if (session.CurrentNode.NodeType == GridFightNodeTypeEnum.CampMonster)
                    yield return 35303030;
                yield break;
        }

        if (GridFight45AffixBuffs.TryGetValue(affixId, out var capturedBuffId))
        {
            yield return capturedBuffId;
            yield break;
        }

        // Future/newer resources can supply their own binding. Do not trust the bundled stale
        // group-3/group-4 mappings blindly: several of them are known to point at 4.4 IDs.
        if (!GameData.GridFightAffixConfigData.TryGetValue(affixId, out var affix)) yield break;
        var isBundledStaleResource = GameData.GridFightAffixConfigData.TryGetValue(3002, out var sentinel) &&
                                     !sentinel.RuleParamList.Contains(35302001u);
        if (isBundledStaleResource && affixId >= 3000) yield break;

        foreach (var buffId in affix.RuleParamList.Where(id => id > 0 && id <= (uint)int.MaxValue).Distinct())
            yield return (int)buffId;
    }

    private static void AddGridFightTeamLeaderBuff(BattleInstance battle)
    {
        var leaderBuff = GameData.MazeBuffData.Values.FirstOrDefault(row =>
            row.InBattleBindingKey == "StageAbility_MazeCommon_EnterBattle_CheckTeamLeader");
        if (leaderBuff == null || battle.Buffs.Any(buff => buff.BuffID == leaderBuff.ID)) return;

        var leaderIndex = battle.LeaderIndexOverride ?? Math.Max(battle.Lineup.GetSlot(battle.Lineup.LeaderAvatarId), 0);
        battle.Buffs.Add(new MazeBuff(leaderBuff.ID, leaderBuff.Lv, leaderIndex)
        {
            WaveFlag = -1,
            // Official GridFight packets carry OwnerIndex but no TargetIndexList for this buff.
            IncludeOwnerInTargetList = false,
        });
    }

    private static void AddOfficialGridFightCombatBuffs(
        BattleInstance battle,
        GridFightSession session,
        GridFightResourceCatalog catalog)
    {
        AddGlobalBuffIfMissing(battle, GridFightBaselineBuffId);
        // 3560xxxx are the season-1 season-talent battle buffs (talents 2011/4031/2061). Season 2 renumbers
        // its talents onto the same IDs with other effects, so do not apply them outside season 1.
        if (!catalog.IsSeasonOne) return;
        AddGlobalBuffIfMissing(battle, FinalBattleTurnHealingBuffId);
        AddGlobalBuffIfMissing(battle, GridFightCombatTalentBuffId);

        // 35602061 is present only in official reward-node battles (node type 1).
        if (session.CurrentNode.NodeType == GridFightNodeTypeEnum.Monster)
            AddGlobalBuffIfMissing(battle, RewardBattleTalentBuffId);
    }

    private static void AddGlobalBuffIfMissing(BattleInstance battle, int buffId)
    {
        if (battle.Buffs.Any(buff => buff.BuffID == buffId)) return;
        battle.Buffs.Add(new MazeBuff(buffId, 1, -1)
        {
            WaveFlag = -1,
            IncludeOwnerInTargetList = false,
        });
    }

    public static GridFightRoleInfo BuildRoleInfo(GridFightSession session, GridFightRoleState role, Gender gender)
    {
        var basic = GameData.GridFightRoleBasicInfoData[role.RoleId];
        var result = new GridFightRoleInfo
        {
            RoleId = role.RoleId,
            AvatarId = ResolveAvatarId(basic, role.Star, gender),
            RoleStar = role.Star,
            Pos = role.Position,
            UniqueId = role.UniqueId,
            BattleExtInfo = new GridFightRoleBattleExtInfo
            {
                // Official 4.5 packets encode the number of this role's currently inactive
                // traits here. Across the supplied captures this rule matches every observed
                // role entry (632/632), including equipment-added traits.
                ModeInfo = new GridFightRoleBattleModeInfo
                {
                    Mode = ResolveRoleBattleMode(session, role),
                    // AvatarSpotLightId: the battle puts the spotlight (主角光环) crown on this unit.
                    IHHNLLAJAGB = session.GetSpotLightIdForRole(role.UniqueId),
                },
                StateInfo = new GridFightRoleBattleStateInfo(),
            },
        };
        GridFightManager.PopulateRolePropertyScales(
            result.ConvertPropertyToFixpoint,
            session.HasCyreneCombatEnhancement);
        session.ApplyInvestmentPropertyScales(result.ConvertPropertyToFixpoint);
        session.ApplyFateDivinerPropertyScales(result.ConvertPropertyToFixpoint, role.RoleId);
        session.ApplySpotLightPropertyScales(result.ConvertPropertyToFixpoint, role.UniqueId);
        session.ApplyPermanentRolePropertyScales(result.ConvertPropertyToFixpoint, role);
        session.ApplyTrailblazeProperties(result.ConvertPropertyToFixpoint);
        result.RoleEquipmentList.Add(session.GetBattleEquipment(role).Select(equipment =>
            new BattleGridFightEquipInfo
            {
                GridFightEquipmentId = equipment.EquipmentId,
                UniqueId = equipment.UniqueId,
            }));
        // 4.7 battles read role-scoped GridFight custom values (SetDynamicValueByGridFightCustomProperty with a target ->
        // GridFightManager._GetOrBindRoleCustomValueLookup) only from GridFightRoleInfo field 14, without falling back to
        // GridFightInfo.role_saved_values; without it Phainon's kindling (薪火, GP_Avatar_S2_Phainon_00) gives no DMG amp.
        if (session.IsSeasonTwoRules)
            foreach (var (key, value) in session.ResolveRoleSavedValues(role, role.RoleId))
                result.BABNHPDGNGB[key] = value;
        return result;
    }

    public static uint ResolveTraitLayer(uint traitId, uint memberCount)
    {
        if (GridFightTraitCompatibility.TryResolveLayer(traitId, memberCount, out var compatibilityLayer))
            return compatibilityLayer;
        return GameData.GridFightTraitLayerData.GetValueOrDefault(traitId)?.Keys
            .Where(layer => layer <= memberCount)
            .DefaultIfEmpty()
            .Max() ?? 0;
    }

    private static void AddEntryBuffs(BattleInstance battle, IReadOnlyList<GridFightRoleState> roles)
    {
        for (var ownerIndex = 0; ownerIndex < roles.Count; ownerIndex++)
        {
            var role = roles[ownerIndex];
            if (!TryResolveEntryMazeSkill(role, out var mazeSkillId)) continue;
            if (battle.Buffs.Any(buff => buff.BuffID == mazeSkillId && buff.OwnerIndex == ownerIndex)) continue;
            battle.Buffs.Add(new MazeBuff(mazeSkillId, 1, ownerIndex)
            {
                WaveFlag = -1,
                IncludeOwnerInTargetList = false,
            });
        }

        if (roles.Count == 0) return;
        var leaderIndex = ResolveGridFightLeaderIndex(battle, roles.Count);
        var leader = roles[leaderIndex];
        if (!GameData.GridFightRoleBasicInfoData.TryGetValue(leader.RoleId, out var basic) ||
            !GameData.AvatarConfigData.TryGetValue((int)basic.AvatarID, out var avatar) ||
            avatar.DamageType == 0)
            return;

        var buffId = (int)avatar.DamageType;
        if (battle.Buffs.Any(buff => buff.BuffID == buffId && buff.OwnerIndex == leaderIndex)) return;
        var skillIndex = ResolveGridFightLeaderSkillIndex(leader);
        var buff = new MazeBuff(buffId, 1, leaderIndex)
        {
            WaveFlag = -1,
            IncludeOwnerInTargetList = false,
        };
        buff.DynamicValues["SkillIndex"] = skillIndex;
        battle.Buffs.Add(buff);
    }

    private static int ResolveGridFightLeaderIndex(BattleInstance battle, int roleCount)
    {
        var index = battle.LeaderIndexOverride ?? battle.Lineup.GetSlot(battle.Lineup.LeaderAvatarId);
        return index >= 0 && index < roleCount ? index : 0;
    }

    private static int ResolveGridFightLeaderSkillIndex(GridFightRoleState leader)
    {
        if (GridFightSkillIndex2RoleIds.Contains(leader.RoleId)) return 2;
        if (GridFightSkillIndex1RoleIds.Contains(leader.RoleId)) return 1;

        return GameData.GridFightRoleStarData.TryGetValue(leader.RoleId << 4 | leader.Star, out var roleStar) &&
               !roleStar.SkillOverrideSrc.Contains(0)
            ? 2
            : 1;
    }

    public static bool TryResolveEntryMazeSkill(GridFightRoleState role, out int mazeSkillId)
    {
        mazeSkillId = 0;

        // Official 4.5 captures omit Asta's entry buff even at one-star foreground.
        // 100902 is an override skill, not a MazeBuffData entry; sending it breaks battle load.
        if (role.RoleId == 1009) return false;

        if (!GameData.GridFightRoleBasicInfoData.TryGetValue(role.RoleId, out var basic)) return false;
        var adventurePlayerId = (int)basic.AvatarID;
        if (GameData.AvatarConfigData.TryGetValue(adventurePlayerId, out var avatar) && avatar.AdventurePlayerID != 0)
            adventurePlayerId = avatar.AdventurePlayerID;
        if (!GameData.AdventurePlayerData.TryGetValue(adventurePlayerId, out var adventure) ||
            adventure.MazeSkillIdList.Count == 0)
            return false;

        if (GameData.GridFightRoleStarData.TryGetValue(role.RoleId << 4 | role.Star, out var roleStar) &&
            roleStar.SkillOverrideSrc.Contains(0))
            mazeSkillId = adventure.MazeSkillIdList.FirstOrDefault(id => roleStar.SkillOverrideSrc.Contains((uint)id));
        if (mazeSkillId == 0) mazeSkillId = adventure.MazeSkillIdList[0];
        // A maze skill id is only a valid battle-entry buff when a MazeBuff row exists for it (MazeBuffData is keyed
        // ID * 10 + level). Overridden skills such as 121002 (Guinaifen), 110302, 140302 have none, and the client
        // NREs sorting MazeBuffData while the battle loads (black screen).
        return mazeSkillId != 0 && GameData.MazeBuffData.ContainsKey(mazeSkillId * 10 + 1);
    }

    public static List<AvatarLineupData> BuildBattleAvatars(BattleInstance battle, GridFightSession session)
    {
        var avatars = new List<AvatarLineupData>();
        foreach (var role in session.Roles
                     .Where(role => role.Position is >= 1 and <= 13)
                     .OrderBy(role => role.Position))
        {
            var avatar = ResolveBattleAvatar(battle, role, battle.Player.Data.CurrentGender,
                isForeground: role.Position <= 4);
            if (avatar == null) continue;
            avatar.AvatarInfo.SetCurHp(10000, true);
            avatars.Add(avatar);
        }
        return avatars;
    }

    internal static BattleAvatar BuildBattleAvatarProto(
        AvatarLineupData avatar,
        PlayerDataCollection collection)
    {
        var proto = avatar.AvatarInfo is FormalAvatarInfo formal && avatar.BattleAvatarId is int battleAvatarId
            ? formal.ToBattleProto(collection, battleAvatarId, avatar.AvatarType)
            : avatar.AvatarInfo.ToBattleProto(collection, avatar.AvatarType);
        ApplyDefaultEnhancement(proto, (int)proto.Id);
        return proto;
    }

    private static void ApplyDefaultEnhancement(BattleAvatar proto, int avatarId)
    {
        if (!GameData.AvatarConfigData.TryGetValue(avatarId, out var avatarConfig)) return;
        var enhancedId = avatarConfig.SkillTree.Keys
            .Where(id => id > 0)
            .DefaultIfEmpty()
            .Min();
        if (enhancedId == 0 || proto.EnhancedId == enhancedId) return;
        if (!avatarConfig.SkillTree.TryGetValue(enhancedId, out var enhancedSkills)) return;

        var levelBySlot = new Dictionary<int, uint>();
        foreach (var skill in proto.SkilltreeList)
        {
            var skillKey = checked((int)skill.PointId * 100 + 1);
            if (!GameData.AvatarSkillTreeConfigData.TryGetValue(skillKey, out var skillConfig)) continue;
            var slot = skillConfig.GetMultiPointId();
            if (slot > 0) levelBySlot[slot] = skill.Level;
        }

        var transformed = new List<AvatarSkillTree>();
        foreach (var skillConfig in enhancedSkills)
        {
            var slot = skillConfig.GetMultiPointId();
            if (slot <= 0 || !levelBySlot.TryGetValue(slot, out var level)) continue;
            transformed.Add(new AvatarSkillTree
            {
                PointId = (uint)skillConfig.PointID,
                Level = Math.Min(level, (uint)skillConfig.MaxLevel),
            });
        }
        if (transformed.Count == 0) return;

        proto.SkilltreeList.Clear();
        proto.SkilltreeList.Add(transformed);
        proto.EnhancedId = (uint)enhancedId;
    }

    private static void PopulateTraitEffects(GridFightTraitInfo traitInfo)
    {
        if (!GameData.GridFightTraitBasicInfoData.TryGetValue(traitInfo.TraitId, out var trait)) return;
        foreach (var effectId in trait.TraitEffectList)
        {
            var effectInfo = new BattleGridFightTraitEffectInfo { EffectId = effectId };
            PopulateTraitBonusLevelInfo(effectInfo);
            traitInfo.TraitEffectList.Add(effectInfo);
        }
    }

    private static void PopulateTraitBonusLevelInfo(BattleGridFightTraitEffectInfo effectInfo)
    {
        if (!GameData.GridFightTraitEffectData.TryGetValue(effectInfo.EffectId, out var effect) ||
            effect.TraitEffectType != GridFightTraitEffectTypeEnum.TraitBonus) return;

        var levelInfo = new GridFightTraitEffectLevelInfo();
        if (GameData.GridFightTraitBonusData.TryGetValue(effectInfo.EffectId, out var bonuses))
        {
            foreach (var (threshold, bonus) in bonuses.OrderBy(entry => entry.Key))
            {
                var dropInfo = new GridFightDropInfo();
                foreach (var combinationId in bonus.BonusParamList)
                {
                    if (!GameData.GridFightCombinationBonusData.TryGetValue(combinationId, out var combination))
                        continue;
                    foreach (var poolId in combination.CombinationBonusList)
                    {
                        if (!GameData.GridFightBasicBonusPoolV2Data.TryGetValue(poolId, out var pool)) continue;
                        if (pool.BonusType != GridFightBonusTypeEnum.Orb) continue;
                        var orbId = pool.BonusTypeParamList.FirstOrDefault();
                        if (orbId == 0) orbId = pool.BonusTypeParam;
                        if (orbId == 0) continue;
                        dropInfo.DropList.Add(new GridFightDropEntry
                        {
                            DropType = GridFightDropType.Orb,
                            ItemId = orbId,
                            Num = 1,
                        });
                    }
                }
                if (dropInfo.DropList.Count > 0)
                    levelInfo.TraitEffectLevelReward[threshold] = dropInfo;
            }
        }
        effectInfo.TraitEffectLevelInfo = levelInfo;
    }

    private static uint ResolveAvatarId(GridFightRoleBasicInfoExcel basic, uint star, Gender gender)
    {
        if (gender != Gender.Woman) return basic.AvatarID;
        var key = (basic.ID << 4) | star;
        // Season 2 Trailblazer roles (800902) have no row of their own; reuse the season-1 role whose id is
        // the avatar id (8009 -> 8010).
        return GameData.GridFightGenderOverrideData.TryGetValue(key, out var genderOverride) ||
               GameData.GridFightGenderOverrideData.TryGetValue((basic.AvatarID << 4) | star, out genderOverride)
            ? genderOverride.AvatarID
            : basic.AvatarID;
    }

    private static AvatarLineupData? ResolveBattleAvatar(BattleInstance battle, GridFightRoleState role, Gender gender,
        bool isForeground)
    {
        if (!GameData.GridFightRoleBasicInfoData.TryGetValue(role.RoleId, out var basic)) return null;
        var avatarId = ResolveAvatarId(basic, role.Star, gender);
        var formal = battle.Player.AvatarManager!.GetFormalAvatar((int)avatarId);
        if (formal != null)
        {
            var avatarType = isForeground && string.Equals(basic.FrontBackType, "Back", StringComparison.Ordinal)
                ? AvatarType.AvatarFormalType
                : AvatarType.AvatarGridFightType;
            return new AvatarLineupData(formal, avatarType, (int)avatarId);
        }
        var trial = battle.Player.AvatarManager.GetTrialAvatarByWorldLevel(
            (int)basic.SpecialAvatarID,
            battle.Player.Data.WorldLevel);
        return trial == null ? null : new AvatarLineupData(trial, AvatarType.AvatarTrialType);
    }

    public static bool IsTrialRole(BattleInstance battle, uint roleId, bool isForeground)
    {
        var role = new GridFightRoleState { RoleId = roleId };
        return ResolveBattleAvatar(battle, role, battle.Player.Data.CurrentGender, isForeground)?.AvatarType ==
               AvatarType.AvatarTrialType;
    }
}
