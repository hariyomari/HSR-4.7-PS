using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Quest;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Avatar;

public class AvatarManager(PlayerInstance player) : BasePlayerManager<AvatarData>(player)
{
    private static readonly Logger Logger = new("Avatar");

    public async ValueTask<AvatarConfigExcel?> AddAvatar(int avatarId, bool sync = true, bool notify = true,
        bool isGacha = false)
    {
        GameData.AvatarConfigData.TryGetValue(avatarId, out var avatarExcel);
        if (avatarExcel == null) return null;
        if (avatarExcel.MaxRank < 6) return null;

        GameData.MultiplePathAvatarConfigData.TryGetValue(avatarId, out var multiPathAvatar);
        var owned = GetFormalAvatar(avatarId);
        if (owned != null)
        {
            // Adding an already owned character must not append an empty row with the same base ID.
            // A path unlock adds only its missing path and keeps the existing skin and equipment.
            if (!owned.PathInfos.ContainsKey(avatarId) && owned.EnsurePathInfo(avatarId))
            {
                MarkDirty();
                if (sync) await Player.SendPacket(new PacketPlayerSyncScNotify(owned));
            }
            return null;
        }
        if (multiPathAvatar != null && multiPathAvatar.BaseAvatarID != avatarId) return null;

        var avatar = new FormalAvatarInfo(multiPathAvatar?.BaseAvatarID ?? avatarId, avatarId, true)
        {
            Level = 1,
            Timestamp = Extensions.GetUnixSec(),
            CurrentHp = 10000,
            CurrentSp = 0
        };

        Data.FormalAvatars.Add(avatar);
        MarkDirty();

        if (avatarExcel.Rarity == RarityEnum.CombatPowerAvatarRarityType5 && avatarExcel.AvatarID <= 3000)
            // add development
            Player.FriendRecordData!.AddAndRemoveOld(new FriendDevelopmentInfoPb
            {
                DevelopmentType = (DevelopmentType)10,
                Params = { { "AvatarId", (uint)avatarExcel.AvatarID } }
            });

        if (sync)
            await Player.SendPacket(new PacketPlayerSyncScNotify(avatar));

        if (notify) await Player.SendPacket(new PacketAddAvatarScNotify(avatar.BaseAvatarId, isGacha));

        return avatarExcel;
    }

    public FormalAvatarInfo? GetFormalAvatar(int avatarId)
    {
        GameData.MultiplePathAvatarConfigData.TryGetValue(avatarId, out var multiPathAvatar);
        var avatar = Data.FormalAvatars.Find(avatar => avatar.BaseAvatarId ==
                                                     (multiPathAvatar?.BaseAvatarID ?? avatarId));
        return avatar == null ? null : BindExRankState(avatar);
    }

    // Match GetFormalAvatar's first-row lookup on older saves without deleting duplicate stored data.
    public IEnumerable<FormalAvatarInfo> GetOwnedAvatars() => Data.FormalAvatars
        .DistinctBy(avatar => avatar.BaseAvatarId).Select(BindExRankState);

    private FormalAvatarInfo BindExRankState(FormalAvatarInfo avatar)
    {
        avatar.ActivatedExRankIds = Data.ExRankIds.GetValueOrDefault(avatar.BaseAvatarId) ?? [];
        return avatar;
    }

    public async ValueTask<Retcode> ChangeSkin(int avatarId, int skinId)
    {
        var avatar = GetFormalAvatar(avatarId);
        if (avatar == null) return Retcode.RetAvatarNotExist;
        var path = avatar.GetPathInfo(avatarId);
        if (path == null && avatarId == avatar.BaseAvatarId) path = avatar.GetCurPathInfo();
        if (path == null) return Retcode.RetAvatarNotExist;
        if (skinId != 0)
        {
            if (!GameData.AvatarSkinData.TryGetValue(skinId, out var skin))
                return Retcode.RetAvatarSkinItemNotExist;
            if (skin.AvatarID != path.PathId) return Retcode.RetAvatarSkinNotMatchAvatar;
            if (Player.PlayerUnlockData!.Skins.GetValueOrDefault(skin.AvatarID)?.Contains(skinId) != true)
                return Retcode.RetAvatarSkinItemNotExist;
        }
        if (path.Skin == skinId) return Retcode.RetSucc;
        path.Skin = skinId;
        MarkDirty();
        await Player.SendPacket(new PacketPlayerSyncScNotify(avatar));
        return Retcode.RetSucc;
    }

    public SpecialAvatarInfo? GetTrialAvatar(int avatarId, bool refresh = false)
    {
        var avatar = Data.TrialAvatars.Find(avatar => avatar.SpecialAvatarId == avatarId);
        if (avatar != null)
        {
            if (refresh)
                Data.TrialAvatars.Remove(avatar);
            else
                return avatar;
        }

        if (!GameData.SpecialAvatarData.TryGetValue(avatarId * 10 + 0, out var excel)) return null;

        var baseAvatarId = excel.AvatarID;
        if (GameData.MultiplePathAvatarConfigData.TryGetValue(baseAvatarId, out var multiple))
            baseAvatarId = multiple.BaseAvatarID;
        avatar = new SpecialAvatarInfo
        {
            SpecialAvatarId = excel.SpecialAvatarID,
            AvatarId = excel.AvatarID,
            BaseAvatarId = baseAvatarId,
            Level = excel.Level,
            Promotion = excel.Promotion
        };

        avatar.PathInfos.Add(excel.AvatarID, new PathInfo(excel.AvatarID)
        {
            Rank = excel.Rank,
            EnhanceId = excel.EnhancedID,
            EquipData = new ItemData
            {
                ItemId = excel.EquipmentID,
                Level = excel.EquipmentLevel,
                Promotion = excel.EquipmentPromotion,
                Rank = excel.EquipmentRank
            }
        });

        if (!GameData.AvatarConfigData.TryGetValue(avatar.BaseAvatarId, out _)) return avatar;
        avatar.GetCurPathInfo().GetSkillTree();
        Data.TrialAvatars.Add(avatar);
        return avatar;
    }

    public SpecialAvatarInfo? GetTrialAvatarByWorldLevel(int specialAvatarId, int worldLevel)
    {
        var avatar = Data.TrialAvatars.Find(a => a.SpecialAvatarId == specialAvatarId);
        if (avatar != null) return avatar;

        SpecialAvatarExcel? excel = null;
        for (var wl = worldLevel; wl >= 0 && excel == null; wl--)
            GameData.SpecialAvatarData.TryGetValue(specialAvatarId * 10 + wl, out excel);
        if (excel == null) return null;

        var baseAvatarId = excel.AvatarID;
        if (GameData.MultiplePathAvatarConfigData.TryGetValue(baseAvatarId, out var multiple))
            baseAvatarId = multiple.BaseAvatarID;
        avatar = new SpecialAvatarInfo
        {
            SpecialAvatarId = excel.SpecialAvatarID,
            AvatarId = excel.AvatarID,
            BaseAvatarId = baseAvatarId,
            Level = excel.Level,
            Promotion = excel.Promotion
        };
        avatar.PathInfos.Add(excel.AvatarID, new PathInfo(excel.AvatarID)
        {
            Rank = excel.Rank,
            EnhanceId = excel.EnhancedID,
            EquipData = new ItemData
            {
                ItemId = excel.EquipmentID,
                Level = excel.EquipmentLevel,
                Promotion = excel.EquipmentPromotion,
                Rank = excel.EquipmentRank
            }
        });

        if (!GameData.AvatarConfigData.TryGetValue(avatar.BaseAvatarId, out _)) return avatar;
        avatar.GetCurPathInfo().GetSkillTree();
        Data.TrialAvatars.Add(avatar);
        return avatar;
    }

    /// <summary>
    ///     Grants multi-path variants whose unlock missions are already finished. Login passes
    ///     <paramref name="notify" /> false because the avatar payload carries the paths; a mission
    ///     finish passes true so the client receives AddMultiPathAvatarScNotify immediately.
    /// </summary>
    public async ValueTask GrantUnlockedStoryPaths(bool notify)
    {
        if (Player.MissionManager == null) return;

        List<int> granted = [];
        HashSet<FormalAvatarInfo> owners = [];
        foreach (var config in GameData.MultiplePathAvatarConfigData.Values.OrderBy(config => config.AvatarID))
        {
            if (config.UnlockConditions.Count == 0) continue;
            if (config.BaseAvatarID == 8001 &&
                PlayerInstance.NormalizeHeroPathId(config.AvatarID, Player.Data.CurrentGender) != config.AvatarID)
                continue;

            var owner = GetFormalAvatar(config.BaseAvatarID);
            if (owner == null || owner.PathInfos.ContainsKey(config.AvatarID)) continue;
            if (!UnlockConditionsFinished(config.UnlockConditions)) continue;
            if (!owner.EnsurePathInfo(config.AvatarID)) continue;

            granted.Add(config.AvatarID);
            owners.Add(owner);
        }

        if (granted.Count == 0) return;
        MarkDirty();
        if (!notify) return;

        foreach (var avatarId in granted)
            await Player.SendPacket(new PacketAddMultiPathAvatarScNotify(avatarId));
        foreach (var owner in owners)
            await Player.SendPacket(new PacketPlayerSyncScNotify(owner));
    }

    private bool UnlockConditionsFinished(List<Condition> conditions)
    {
        foreach (var condition in conditions)
        {
            if (!int.TryParse(condition.Param, out var id)) return false;
            switch (condition.Type)
            {
                case ConditionTypeEnum.FinishMainMission:
                    if (id == 0 || Player.MissionManager!.GetMainMissionStatus(id) != MissionPhaseEnum.Finish)
                        return false;
                    break;
                case ConditionTypeEnum.FinishSubMission:
                    if (id == 0 || Player.MissionManager!.GetSubMissionStatus(id) != MissionPhaseEnum.Finish)
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>The opening main mission, which holds the nickname and gender prompt.</summary>
    public const int OpeningMainMissionId = 1000101;

    /// <summary>The prologue sub-mission that waits for the nickname and gender choice.</summary>
    public const int NameAndGenderSubMissionId = 100010134;

    public FormalAvatarInfo? GetHero()
    {
        return Data.FormalAvatars.Find(avatar => avatar.BaseAvatarId == 8001);
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
            lineup.BaseAvatars?.Any(avatar => avatar.BaseAvatarId == 8001) == true);

    /// <summary>
    ///     登录前校正开拓者当前命途（yuukisr_go a532fa0）：<c>cur_multi_path_avatar_type</c> 必须是一个开拓者命途变体，
    ///     且在已设定性别时与玩家性别一致。旧存档可能留下非命途 id 或与性别相反的变体，客户端按它解析队长时
    ///     找不到对应命途数据，<c>TeamManager._CreateMemberEntity</c> 抛 IndexOutOfRange，登录停在加载界面。
    ///     只修内存数据并标脏，由下次存档持久化；数据已一致时不做任何改动。返回是否发生了修正。
    /// </summary>
    public bool NormalizeHeroCurPath()
    {
        var hero = GetHero();
        if (hero == null) return false;

        var gender = Player.Data.CurrentGender;
        // 剧情性别选择之前客户端还没决定主角形象，不按默认性别去改。
        var genderKnown = gender is Gender.Man or Gender.Woman && !IsHeroGenderPending;
        var cur = hero.AvatarId;

        if (!IsHeroPath(cur))
        {
            // 非命途 id：回落到已拥有的、与性别相符的编号最小的命途；都没有就用该性别的毁灭命途。
            var owned = hero.PathInfos.Keys.Where(IsHeroPath)
                .Where(id => !genderKnown || PlayerInstance.NormalizeHeroPathId(id, gender) == id)
                .OrderBy(id => id).ToList();
            cur = owned.Count > 0 ? owned[0] : gender == Gender.Man ? 8001 : 8002;
        }
        else if (genderKnown)
        {
            cur = PlayerInstance.NormalizeHeroPathId(cur, gender);
        }

        var hasPath = hero.PathInfos.ContainsKey(cur);
        if (cur == hero.AvatarId && Player.Data.CurBasicType == cur && hasPath) return false;
        if (!hasPath && !hero.EnsurePathInfo(cur)) return false;

        Logger.Warn($"Player {Player.Uid}: Trailblazer current path {hero.AvatarId} retargeted to {cur}");
        hero.AvatarId = cur;
        Player.Data.CurBasicType = cur;
        MarkDirty();
        return true;
    }

    public static bool IsHeroPath(int avatarId)
    {
        return GameData.MultiplePathAvatarConfigData.TryGetValue(avatarId, out var config) &&
               config.BaseAvatarID == 8001;
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
        Player.Data.CurrentGender = gender == Gender.Woman ? Gender.Woman : Gender.Man;
        Player.Data.IsGenderSet = true;
        if (!heroHidden)
        {
            await Player.ChangeAvatarPathType(8001, MultiPathAvatarTypeEnum.Warrior);
            return;
        }

        var hero = GetHero();
        if (hero == null) return;
        var pathId = PlayerInstance.NormalizeHeroPathId(8001, Player.Data.CurrentGender);
        Player.Data.CurBasicType = pathId;
        hero.AvatarId = pathId;
        hero.SetCurSp(0, Player.LineupManager!.GetCurLineup()!.IsExtraLineup());
        hero.CheckPathSkillTree();
        MarkDirty();
        await Player.SendPacket(new PacketPlayerSyncScNotify(hero));
    }

    public async ValueTask ReforgeRelic(int uniqueId)
    {
        var relic = Player.InventoryManager!.Data.RelicItems.FirstOrDefault(x => x.UniqueId == uniqueId);
        if (relic == null) return;
        await Player.InventoryManager!.RemoveItem(238, 1);

        var subAffixesClone = relic.SubAffixes.Select(x => x.Clone()).ToList();

        var levelUpCnt = 0;
        foreach (var subAffix in relic.SubAffixes)
        {
            levelUpCnt += subAffix.Count - 1;
            subAffix.Count = 1;
            subAffix.Step = 0;
        }

        relic.IncreaseRandomRelicSubAffix(levelUpCnt);
        relic.ReforgeSubAffixes = relic.SubAffixes;
        relic.SubAffixes = subAffixesClone;

        await Player.SendPacket(new PacketPlayerSyncScNotify(relic));
    }

    public async ValueTask ConfirmReforgeRelic(int uniqueId, bool isCancel)
    {
        var relic = Player.InventoryManager!.Data.RelicItems.FirstOrDefault(x => x.UniqueId == uniqueId);
        if (relic == null) return;
        if (relic.ReforgeSubAffixes.Count == 0) return;

        if (!isCancel)
            relic.SubAffixes = relic.ReforgeSubAffixes;
        relic.ReforgeSubAffixes = [];

        await Player.SendPacket(new PacketPlayerSyncScNotify(relic));
    }

    public List<RelicSmartWearPlan> GetRelicPlan(int avatarId)
    {
        var planList = new List<RelicSmartWearPlan>();
        foreach (var plan in Player.InventoryManager!.Data.RelicPlans)
        {
            if (plan.Value.EquipAvatar != avatarId) continue;
            if (plan.Value.InsideRelic.Count == 0 && plan.Value.OutsideRelic.Count == 0) continue;

            planList.Add(new RelicSmartWearPlan
            {
                AvatarId = (uint)avatarId,
                UniqueId = (uint)plan.Key,
                OutsideRelicList = { plan.Value.OutsideRelic.Select(x => (uint)x) },
                InsideRelicList = { plan.Value.InsideRelic.Select(x => (uint)x) }
            });
        }

        return planList;
    }

    public RelicSmartWearPlan AddRelicPlan(RelicSmartWearPlan plan)
    {
        var curUnique = Player.InventoryManager!.Data.RelicPlans.Keys;
        var uniqueId = curUnique.Count > 0 ? curUnique.Max() + 1 : 1;
        Player.InventoryManager!.Data.RelicPlans[uniqueId] = new RelicPlanData
        {
            EquipAvatar = (int)plan.AvatarId,
            InsideRelic = [.. plan.InsideRelicList.Select(x => (int)x)],
            OutsideRelic = [.. plan.OutsideRelicList.Select(x => (int)x)]
        };

        plan.UniqueId = (uint)uniqueId;
        return plan;
    }

    public void UpdateRelicPlan(RelicSmartWearPlan plan)
    {
        Player.InventoryManager!.Data.RelicPlans[(int)plan.UniqueId] = new RelicPlanData
        {
            EquipAvatar = (int)plan.AvatarId,
            InsideRelic = [.. plan.InsideRelicList.Select(x => (int)x)],
            OutsideRelic = [.. plan.OutsideRelicList.Select(x => (int)x)]
        };
    }

    public void DeleteRelicPlan(int uniqueId)
    {
        Player.InventoryManager!.Data.RelicPlans[uniqueId] = new RelicPlanData();
    }

    // ---- 遗器预设（4.5 智能装配保存下来的方案）----
    // 4.5 客户端的智能装配走的是 RelicPreset 这套协议，和上面 2.3 时代的 RelicSmartWear 方案是两套东西。

    public List<PFCKBFLDHNI> GetRelicPresetPlans(int avatarId)
    {
        return
        [
            .. Player.InventoryManager!.Data.RelicPresetPlans
                .Where(plan => plan.Value.AvatarId == avatarId)
                .Select(plan => BuildRelicPresetPlan(plan.Key, plan.Value))
        ];
    }

    /// <summary>
    ///     新建方案。角色必须已拥有、遗器列表非空且每件遗器都在背包里（yuukisr_go c4f5979 同样拒绝空方案），
    ///     否则返回 null 且不写存档。
    /// </summary>
    public PFCKBFLDHNI? AddRelicPresetPlan(uint avatarId, string name, IEnumerable<uint> relicIds,
        RelicPresetPlanSourceType sourceType)
    {
        var relicIdList = relicIds.ToList();
        if (avatarId == 0 || GetFormalAvatar((int)avatarId) == null) return null;
        if (relicIdList.Count == 0 || !OwnsRelics(relicIdList)) return null;

        var plans = Player.InventoryManager!.Data.RelicPresetPlans;
        var uniqueId = plans.Count > 0 ? plans.Keys.Max() + 1 : 1;
        var plan = new RelicPresetPlanData
        {
            AvatarId = (int)avatarId,
            Name = name,
            SourceType = (int)sourceType,
            RelicIds = [.. relicIdList.Select(x => (int)x)]
        };

        plans[uniqueId] = plan;
        MarkDirty();
        return BuildRelicPresetPlan(uniqueId, plan);
    }

    /// <summary>
    ///     一次请求要么改名要么换遗器（4.6 dump 里这两个字段是 oneof），请求带了什么就改什么。
    /// </summary>
    public bool UpdateRelicPresetPlan(int uniqueId, HPPOINFLEPJ? relicList, string? name)
    {
        if (!Player.InventoryManager!.Data.RelicPresetPlans.TryGetValue(uniqueId, out var plan)) return false;
        // 两项都没带的请求没有可改的内容，不当作成功。
        if (relicList == null && string.IsNullOrEmpty(name)) return false;
        if (relicList != null && (relicList.RelicIds.Count == 0 || !OwnsRelics(relicList.RelicIds))) return false;

        if (relicList != null)
        {
            plan.RelicIds = [.. relicList.RelicIds.Select(x => (int)x)];
            plan.SourceType = (int)relicList.SourceType;
        }

        if (!string.IsNullOrEmpty(name)) plan.Name = name;
        MarkDirty();
        return true;
    }

    private bool OwnsRelics(IEnumerable<uint> relicIds)
    {
        var owned = Player.InventoryManager!.Data.RelicItems.Select(x => x.UniqueId).ToHashSet();
        // 0 视为空槽位，不当作未拥有的遗器。
        return relicIds.All(id => id == 0 || owned.Contains((int)id));
    }

    public bool DeleteRelicPresetPlan(int uniqueId)
    {
        if (!Player.InventoryManager!.Data.RelicPresetPlans.Remove(uniqueId)) return false;
        MarkDirty();
        return true;
    }

    private static PFCKBFLDHNI BuildRelicPresetPlan(int uniqueId, RelicPresetPlanData plan)
    {
        var proto = new PFCKBFLDHNI
        {
            UniqueId = (uint)uniqueId,
            AvatarId = (uint)plan.AvatarId,
            Name = plan.Name,
            SourceType = (RelicPresetPlanSourceType)plan.SourceType
        };

        proto.RelicIds.AddRange(plan.RelicIds.Select(x => (uint)x));
        return proto;
    }
}
