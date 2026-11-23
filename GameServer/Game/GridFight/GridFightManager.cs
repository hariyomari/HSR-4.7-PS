using Google.Protobuf;
using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.GridFight;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.GridFight.Battle;
using March7thHoney.GameServer.Game.GridFight.Persistence;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.GridFight;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightManager : BasePlayerManager
{
    private static readonly Logger Logger = new("GridFight");

    private uint _nextSessionUniqueId = 1;
    private readonly Lazy<GridFightResourceCatalog> _catalog;
    private readonly GridFightData? _data;
    private GridFightDamageSttInfo _lastBattleDamageStatistics = new();

    public GridFightResourceCatalog Catalog => _catalog.Value;
    public GridFightSession? Session { get; private set; }

    public GridFightManager(PlayerInstance player, Random? random = null) : base(player)
    {
        _catalog = new Lazy<GridFightResourceCatalog>(() =>
        {
            var catalog = new GridFightResourceCatalog(random);
            var errors = catalog.Validate();
            if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
            return catalog;
        });
        // Isolated tools build managers without a database; their runs simply stay in memory.
        _data = DatabaseHelper.Instance?.GetInstanceOrCreateNew<GridFightData>(player.Uid);
        if (_data != null) _nextSessionUniqueId = Math.Max(_nextSessionUniqueId, _data.NextSessionUniqueId);
        RestoreRun();
    }

    public bool Start(uint season, uint divisionId, bool isOverlock)
    {
        if (season != Catalog.CurrentSeason ||
            !GameData.GridFightDivisionInfoData.TryGetValue(divisionId, out var division) ||
            division.SeasonID != season)
            return false;
        Session = new GridFightSession(Catalog, season, divisionId, isOverlock, _nextSessionUniqueId++);
        HasSettledSession = false;
        _lastBattleDamageStatistics = new GridFightDamageSttInfo();
        Persist();
        return true;
    }

    /// <summary>
    ///     Writes the live run to <see cref="GridFightData" />. Called after every GridFight request, battle settlement,
    ///     console command and on logout, always on the thread that mutated the run; the database flush only copies the
    ///     resulting string.
    /// </summary>
    public void Persist()
    {
        if (_data == null) return;
        try
        {
            var session = Session;
            if (session == null)
            {
                _data.RunInstance = null;
            }
            else
            {
                var snapshot = session.CaptureSnapshot();
                snapshot.LastBattleDamageStatistics = _lastBattleDamageStatistics.ToByteArray();
                _data.RunInstance = Convert.ToBase64String(MemoryPackSerializer.Serialize(snapshot));
            }
            _data.NextSessionUniqueId = _nextSessionUniqueId;
            DatabaseHelper.MarkDirty(Player.Uid);
        }
        catch (Exception e)
        {
            // Keep the previous blob: a failed capture must never break the request that triggered it.
            Logger.Error(I18NManager.Translate("Server.Log.GridFightSaveFailed", Player.Uid.ToString()), e);
        }
    }

    private void RestoreRun()
    {
        if (_data == null || string.IsNullOrEmpty(_data.RunInstance)) return;
        try
        {
            var snapshot = MemoryPackSerializer.Deserialize<GridFightRunSnapshot>(Convert.FromBase64String(_data.RunInstance)) ??
                           throw new InvalidDataException("Empty GridFight snapshot.");
            var session = GridFightSession.Restore(Catalog, snapshot);
            var statistics = new GridFightDamageSttInfo();
            if (snapshot.LastBattleDamageStatistics.Length > 0)
                statistics.MergeFrom(snapshot.LastBattleDamageStatistics);
            Session = session;
            _lastBattleDamageStatistics = statistics;
            _nextSessionUniqueId = Math.Max(_nextSessionUniqueId, session.UniqueId + 1);
        }
        catch (Exception e)
        {
            // A blob that no longer fits this build or resource cache is dropped instead of crashing the login.
            Logger.Warn(I18NManager.Translate("Server.Log.GridFightRunDropped", Player.Uid.ToString(), e.Message));
            Session = null;
            _data.RunInstance = null;
            DatabaseHelper.MarkDirty(Player.Uid);
            return;
        }

        if (Session is { Phase: GridFightSessionPhase.InBattle } inBattle)
        {
            // The battle died with the previous connection or process. Settle it like BATTLE_END_QUIT; the client
            // receives the unlocked state through its next GetData, so no sync is sent here.
            var settlement = inBattle.ResolveBattle(false, GridFightBattleStatistics.Empty);
            inBattle.ClearMortenaxBladeBattleEffects();
            // A lost battle can end the run (squad HP 0, or the last node). Its settle page can no longer be shown,
            // so the run is closed here like EndBattle would after its settle notify.
            if (settlement.Success && (settlement.Defeated || !settlement.RouteAdvanced))
            {
                Logger.Debug($"The saved GridFight run of uid {Player.Uid} ended with its interrupted battle.");
                ClearSession(settled: true);
            }
            Persist();
        }
    }

    /// <summary>
    ///     Raises GridFightFinishGamePlay for a settled run (Param 1 standard, 2 overclock). Runs settle by victory or
    ///     by a quit settle, which is also how a lost run leaves its settle page.
    /// </summary>
    public async ValueTask RecordGameFinished(GridFightSession session)
    {
        if (Player.MissionManager == null || Player.QuestManager == null) return;
        await Player.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.GridFightFinishGamePlay, 1,
            session.IsOverlock ? 2 : 1);
    }

    /// <summary>
    ///     True after a run was settled by the server itself (victory or defeat) until the next run starts; a quit-settle
    ///     request the settle page sends afterwards is then answered instead of failed.
    /// </summary>
    public bool HasSettledSession { get; private set; }

    public void ClearSession(bool settled = false)
    {
        Session = null;
        HasSettledSession = settled;
        _lastBattleDamageStatistics = new GridFightDamageSttInfo();
        Persist();
    }

    public GridFightSystemInfo BuildSystemInfo()
    {
        var handbook = new GridFightHandBookInfo
        {
            MonsterInfo = new GridFightHandBookMonsterInfo(),
            RoleInfo = new GridFightHandBookRoleInfo(),
            AugmentInfo = new GridFightHandBookAugmentInfo(),
            PortalInfo = new GridFightHandBookPortalInfo(),
            EquipInfo = new GridFightHandBookEquipInfo(),
        };
        handbook.MonsterInfo.MonsterIdList.Add(GameData.GridFightCampData.Values
            .Where(row => GridFightSeasonScope.IsCampInSeason(row, Catalog.CurrentSeason))
            .Select(row => row.ID)
            .Order());
        handbook.RoleInfo.RoleIdList.Add(GameData.GridFightRoleBasicInfoData.Values
            .Where(row => row.SeasonID == Catalog.CurrentSeason && row.IsInBook)
            .Select(row => row.ID)
            .Order());
        handbook.AugmentInfo.SeenAugmentIdList.Add(Catalog.AugmentPool);
        handbook.AugmentInfo.UnlockedAugmentIdList.Add(Catalog.AugmentPool);
        var portalIds = GameData.GridFightPortalBuffData.Values
            .Where(row => row.IfInBook && GridFightSeasonScope.IsPortalInSeason(row.ID, Catalog.CurrentSeason))
            .Select(row => row.ID)
            .Order()
            .ToList();
        handbook.PortalInfo.SeenPortalIdList.Add(portalIds);
        handbook.PortalInfo.UnlockedPortalIdList.Add(portalIds);
        handbook.EquipInfo.GridFightItemList.Add(GridFightSeasonScope.Equipment(Catalog.CurrentSeason)
            .Select(row => row.ID)
            .Order());
        // Spotlight handbook (主角光环图鉴): the only 4.7 handbook addition with two id lists (seen/unlocked, like
        // augments and portals); the single-list LLBMKOILIDN/ODKODPBCDKD remain unidentified (likely the PetPal pages).
        if (Catalog.CurrentSeason >= 2)
        {
            var spotLightIds = GridFightSeasonTwoTables.SpotLights.Select(row => row.Id).Order().ToList();
            handbook.KJIJEGAHLGB = new GNIIDINFPDO();
            handbook.KJIJEGAHLGB.JHGCJKCHNCO.Add(spotLightIds);
            handbook.KJIJEGAHLGB.MOOIOOBHGKB.Add(spotLightIds);
        }

        var seasonTalent = new GridFightTalentInfo();
        seasonTalent.TalentIdList.Add(GameData.GridFightSeasonTalentData.Values
            .Where(row => row.SeasonID == Catalog.CurrentSeason)
            .Select(row => row.ID)
            .Order());
        var permanentTalent = new GridFightTalentInfo();
        permanentTalent.TalentIdList.Add(GameData.GridFightTalentData.Keys.Order());
        var permanentInfo = new GridFightPermanentGameInfo { Talent = permanentTalent };
        // Season entry requires FBJMBJKMAJA progress per tutorial division, not tutorial_completion alone.
        // 4.8 dropped the last completed stage (ENECKKGPKHI) and GridFightLastTutorialFinishScNotify.
        // Leave the new bool PMFHAPIFFPO unset; its meaning is unknown.
        foreach (var (divisionId, completion, tutorialTask) in Catalog.CompletedTutorialStages)
            permanentInfo.FBJMBJKMAJA[divisionId] = new GLHCHCJMFGA
            {
                EABKMELHIML = divisionId,
                GDAKCOICIDA = completion,
                MPJPLKKJNGE = tutorialTask,
            };

        return new GridFightSystemInfo
        {
            StaticInfo = new GridFightStaticGameInfo
            {
                Exp = new GridFightSeasonExpInfo
                {
                    Exp = Catalog.SeasonExpLimit,
                },
                TutorialCompletion = Catalog.TutorialCompletion,
                Handbook = handbook,
                DivisionId = Catalog.HighestDivision.ID,
                Season = Catalog.CurrentSeason,
                SubSeason = Catalog.CurrentSubSeason,
                SeasonTalent = seasonTalent,
            },
            PermanentInfo = permanentInfo,
        };
    }

    public GridFightCurrentInfo? BuildCurrentInfo()
    {
        var session = Session;
        if (session == null) return null;
        ResetSeasonTwoSentEffects();
        ObserveRoleCapacity(session);
        var current = new GridFightCurrentInfo
        {
            GameData = BuildGameData(),
            IsOverlock = session.IsOverlock,
            DivisionId = session.DivisionId,
            Season = session.Season,
            UniqueId = session.UniqueId,
            ContextData = new GridFightContextData(),
        };
        current.ContextData.InitialEquipmentIdList.Add(Catalog.InitialEquipmentIds);
        var pending = BuildPendingAction();
        if (pending != null) current.PendingAction = pending;
        current.GameInfoList.Add(BuildBasicInfo(pending != null && !session.IsSpotLightSelecting));
        current.GameInfoList.Add(new GridFightGameInfo { TeamInfo = BuildTeamInfo() });
        current.GameInfoList.Add(new GridFightGameInfo { ItemsInfo = BuildItemsInfo() });
        current.GameInfoList.Add(new GridFightGameInfo { ShopInfo = BuildShopInfo() });
        current.GameInfoList.Add(new GridFightGameInfo { OrbInfo = BuildOrbInfo() });
        current.GameInfoList.Add(new GridFightGameInfo { LevelInfo = BuildLevelInfo() });
        var augmentInfo = new GridFightGameAugmentSync();
        augmentInfo.SyncAugmentInfo.Add(session.ActiveInvestmentIds.Select(BuildInvestment));
        current.GameInfoList.Add(new GridFightGameInfo { AugmentInfo = augmentInfo });
        current.GameInfoList.Add(new GridFightGameInfo { TraitInfo = BuildGameTraitInfo() });
        current.GameInfoList.Add(new GridFightGameInfo());
        current.GameInfoList.Add(new GridFightGameInfo { ContextValueInfo = BuildContextValueInfo(session) });
        if (session.IsSeasonTwoRules)
            current.GameInfoList.Add(new GridFightGameInfo { LEGMAJLHBLA = BuildSpotLightState(session) });
        current.GameInfoList.Add(new GridFightGameInfo { PrayQuestInfo = new GridFightPrayQuestInfo() });
        if (session.PetPalId != 0)
        {
            var petPal = new FBKIFPOEGGO();
            petPal.BBEEGMDAMIB.Add(BuildPetPalEffects(session));
            current.GameInfoList.Add(new GridFightGameInfo { OGKMAKNOPOD = petPal });
        }
        return current;
    }

    // Field identities follow the client GridFightPetPalEffectRepository.Sync, which passes NDAHDHKGNBG (9) to
    // GridFightPetPalConfig.GetConfig and OKGGFNJJLNG (8) to GridFightPetPalEffectConfig.GetConfig; MOCLKEPPIGA (1)
    // marks it active. Swapping them leaves the S2 preparation page without a partner and blacks out the start.
    private static IEnumerable<LKHAJCACPBH> BuildPetPalEffects(GridFightSession session)
    {
        if (session.PetPalId == 0) yield break;
        yield return new LKHAJCACPBH
        {
            NDAHDHKGNBG = session.PetPalId,
            OKGGFNJJLNG = session.PetPalEffectId,
            MOCLKEPPIGA = true,
        };
    }

    public GridFightSettleNotify BuildSettleNotify(GridFightSettleReason reason)
    {
        var session = Session ?? throw new InvalidOperationException("GridFight session is not active");
        var context = new GridFightContextData();
        context.InitialEquipmentIdList.Add(Catalog.InitialEquipmentIds);

        var avatars = new GridFightSettleAvatarInfo();
        avatars.GridFightAvatarList.Add(session.Roles
            .Select(role => role.RoleId)
            .Distinct()
            .Order());

        var bosses = new GridFightBossInfo();
        bosses.MonsterList.Add(session.Bosses.Select(monster => new GridFightMonsterInfo
        {
            MonsterId = monster.MonsterID,
            RoleStar = 1,
        }));

        var finish = new GridFightFinishInfo
        {
            Reason = reason,
            ContextData = context,
            AvatarInfo = avatars,
            IsOverlock = session.IsOverlock,
            SettleRank = reason == GridFightSettleReason.Victory
                ? Catalog.GetSettleRank(session.LineupHp)
                : Catalog.GetMinimumSettleRank(),
            Basic = new GridFightSettleBasicInfo
            {
                RouteId = Catalog.StandardRouteId,
                ChapterId = session.ChapterId,
                SectionId = session.SectionId,
                DivisionId = session.DivisionId,
                LineupHp = session.LineupHp,
                MaxLineupHp = session.MaxLineupHp,
                BattlesFinished = session.BattlesFinished,
            },
            BossInfo = bosses,
        };
        finish.AffixInfoList.Add(session.AffixIds.Select(id => new GridFightAffixInfo { AffixId = id }));
        finish.BBEEGMDAMIB.Add(BuildPetPalEffects(session));
        finish.GridGameRoleList.Add(session.Roles
            .OrderBy(role => role.Position)
            .ThenBy(role => role.UniqueId)
            .Select(BuildRole));
        if (session.PepeNpc != null)
            finish.GridGameNpcList.Add(BuildPepeNpc(session.PepeNpc));
        if (session.AstaDogNpc != null)
            finish.GridGameNpcList.Add(BuildPepeNpc(session.AstaDogNpc));
        finish.GridGameNpcList.Add(session.LizhiNpcs.Select(BuildPepeNpc));
        finish.GridGameNpcList.Add(session.OwlbertNpcs.Select(BuildPepeNpc));
        finish.GridFightEquipmentList.Add(session.Equipments
            .OrderBy(equipment => equipment.UniqueId)
            .Select(BuildEquipment));
        finish.GridFightTraitInfo.Add(BuildGameTraitInfo().GridFightTraitInfo);
        finish.SyncAugmentInfo.Add(session.ActiveInvestmentIds.Select(BuildInvestment));
        finish.RoleStatistics.Add(_lastBattleDamageStatistics.RoleStatistics.Select(row => row.Clone()));
        finish.TraitStatistics.Add(_lastBattleDamageStatistics.TraitStatistics.Select(row => row.Clone()));
        finish.AugmentStatistics.Add(_lastBattleDamageStatistics.AugmentStatistics.Select(row => row.Clone()));
        finish.PortalBuffInfoList.Add(session.ActivePortalIds.Select(portalId =>
            new GridFightGamePortalBuffInfo
            {
                PortalBuffId = portalId,
            }));

        return new GridFightSettleNotify
        {
            // Field 1 becomes the player's division on the client (_OnGridFightSettleNotify ->
            // GridFightDivisionInfo.Sync). Every division is unlocked here (the system info reports the highest one),
            // so it must stay the highest: the played difficulty would lock every harder one until the next login.
            // The played difficulty is in finish_info.basic.division_id.
            DivisionId = Catalog.HighestDivision.ID,
            HighestDivisionId = Catalog.HighestDivision.ID,
            SeasonExp = Catalog.SeasonExpLimit,
            FinishInfo = finish,
        };
    }

    private GridFightGameData BuildGameData()
    {
        var levelProgression = new GridFightPlayerLevelProgressionInfo();
        foreach (var row in GameData.GridFightPlayerLevelData.Values.OrderBy(row => row.PlayerLevel))
            levelProgression.LevelUpExpByLevel[row.PlayerLevel] = row.LevelUpExp;

        var data = new GridFightGameData();
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 4,
            EmptyInfo = new GridFightEmptyGameItemInfo(),
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type105, 10101),
        });
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 8,
            PlayerLevelProgression = levelProgression,
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type105, 10601),
        });
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 9,
            RoleHistoryInfo = new GridFightRoleHistoryInfo(),
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type5, 0),
        });
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 19,
            SingleValueInfo = new GridFightSingleValueItemInfo(),
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type111, 8009),
        });
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 56,
            CountInfo = new GridFightCountItemInfo { Count = 2 },
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type107, 3011),
        });
        data.GameItemList.Add(new GridFightGameItemInfo
        {
            UniqueId = 94,
            CountInfo = new GridFightCountItemInfo { Count = 1 },
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.Type107, 4061),
        });
        if (Session is { } portalSession)
            data.GameItemList.Add(portalSession.BuildPortalRuntimeGameItems());
        if (Session?.HackConsole is { } hackConsole)
            data.GameItemList.Add(BuildHackConsoleItem(hackConsole));
        if (Session is { FateOption5GameItemUniqueId: > 0 } fateSession)
            data.GameItemList.Add(BuildFateDivinerGameItem(fateSession, true));
        if (Session != null)
        {
            _sentSeasonTwoItems.Clear();
            foreach (var item in BuildSeasonTwoTraitItems(Session).ToList())
            {
                data.GameItemList.Add(item);
                _sentSeasonTwoItems[item.UniqueId] = Google.Protobuf.MessageExtensions.ToByteString(item);
            }
        }
        if (Session != null && BuildAhaModifierItem(Session) is { } ahaModifier)
        {
            data.GameItemList.Add(ahaModifier);
            _sentAhaProgress = string.Join(',', ahaModifier.GEKCCCPGBGJ.GridFightTraitInfo
                .Select(entry => $"{entry.TraitId}:{entry.Progress}"));
        }
        if (Session != null && BuildPamKingModifierItem(Session) is { } pamKingModifier)
        {
            data.GameItemList.Add(pamKingModifier);
            _sentPamKingProgress = $"{pamKingModifier.UniqueId}:{pamKingModifier.ONKMJAGNECC.IANFMIJBKAF}";
        }
        return data;
    }

    private static GridFightGameItemDescriptor BuildGameItemDescriptor(GridFightGameItemCategory category, uint parameter)
    {
        var descriptor = new GridFightGameItemDescriptor { Category = category };
        descriptor.ParameterList.Add(parameter);
        return descriptor;
    }

    public GridFightPendingAction? BuildPendingAction()
    {
        var session = Session;
        if (session == null) return null;
        if (session.EquipmentRecommendation is { } recommendation)
        {
            var action = new GridFightRecommendEquipmentActionInfo();
            action.EquipmentIdList.Add(recommendation.EquipmentIds);
            return new GridFightPendingAction
            {
                QueuePosition = recommendation.QueuePosition,
                RecommendEquipmentAction = action,
            };
        }
        if (session.FateDivinerPending is { } fate)
            return BuildFateDivinerPendingAction(fate);
        if (session.HeadPlayerPending is { } headPlayer)
        {
            var action = new GridFightTraitEffectSelectEnhanceActionInfo
            {
                TraitId = GridFightSpecialFeatures.SilverWolfTraitId,
                EffectId = headPlayer.EffectId,
                IsOptional = false,
            };
            action.EnhanceIndexList.Add(Enumerable.Repeat(0u, headPlayer.OptionIds.Count));
            action.EnhanceOptionIdList.Add(headPlayer.OptionIds);
            return new GridFightPendingAction
            {
                QueuePosition = headPlayer.QueuePosition,
                TraitEffectSelectEnhanceAction = action,
            };
        }
        if (BuildSeasonTwoPendingAction(session) is { } seasonTwoPending) return seasonTwoPending;
        var pending = new GridFightPendingAction { QueuePosition = session.PendingQueuePosition };
        switch (session.Phase)
        {
            case GridFightSessionPhase.SelectingPortal:
                // Same shape as the investment offer: the free allowance stays fixed, reroll_count counts uses.
                pending.PortalAction = new GridFightPortalBuffActionInfo
                {
                    FreeRerollCount = GridFightSession.PortalFreeRerollCount,
                    RerollCount = session.PortalRerollCount,
                };
                pending.PortalAction.PortalIdList.Add(session.PortalOffer);
                break;
            case GridFightSessionPhase.SelectingNestedPortal:
                pending.PortalAction = new GridFightPortalBuffActionInfo();
                pending.PortalAction.PortalIdList.Add(session.PortalOffer);
                if (session.PortalOffer.Count == 1)
                    pending.PortalAction.SelectCount = session.PortalOffer[0];
                break;
            case GridFightSessionPhase.AwaitingInitialRound:
            case GridFightSessionPhase.AwaitingBattleRound:
            case GridFightSessionPhase.AwaitingSupplyRound:
                pending.RoundBeginAction = new GridFightRoundBeginActionInfo();
                break;
            case GridFightSessionPhase.SelectingInvestment:
                pending.InvestmentAction = new GridFightAugmentActionInfo();
                pending.InvestmentAction.PendingInvestmentList.Add(session.InvestmentOffer.Select(augmentId =>
                    session.IsGoldFuturesInvestmentPending
                        ? new GridFightPendingAugmentInfo { AugmentId = augmentId }
                        : new GridFightPendingAugmentInfo
                        {
                            AugmentId = augmentId,
                            FreeRerollCount = session.InvestmentFreeRerollCount,
                            RerollCount = Math.Min(session.GetInvestmentRerollCount(augmentId), session.InvestmentFreeRerollCount),
                        }));
                break;
            case GridFightSessionPhase.SelectingSupply:
                pending.SupplyAction = new GridFightSupplyActionInfo
                {
                    SelectCount = 1,
                    FreeRerollCount = 1,
                    RerollCount = session.SupplyRerollCount,
                };
                pending.SupplyAction.SupplyRoleInfoList.Add(session.SupplyOffer.Select(offer =>
                {
                    // The 4.6.51 client builds each draft card from field 13 (top drop, must be a
                    // Role/RoleWithEquip entry) and reads equipment from the field 6 drop list;
                    // a missing top drop NREs in GridFightBonusDropData.Create and the run hangs.
                    var role = new GridFightSupplyRoleInfo
                    {
                        RoleId = offer.RoleId,
                        OPPFPECOBEF = new GridFightDropEntry
                        {
                            DropType = GridFightDropType.Role,
                            ItemId = offer.RoleId,
                            Num = 1,
                            Param = new GridFightDropParam { RoleStar = Math.Max(offer.Star, 1u) },
                        },
                    };
                    foreach (var equipmentId in offer.ExtraEquipmentIds.Prepend(offer.EquipmentId).Where(id => id != 0))
                    {
                        role.GridFightItemList.Add(equipmentId);
                        role.DHLBEFLOOPM.Add(new GridFightDropEntry
                        {
                            DropType = GridFightDropType.Item,
                            ItemId = equipmentId,
                            Num = 1,
                        });
                    }
                    return role;
                }));
                break;
            case GridFightSessionPhase.SelectingEncounter:
                pending.EliteBranchAction = new GridFightEliteBranchActionInfo
                {
                    FreeRerollCount = 1,
                    RerollCount = session.EncounterRerollCount,
                };
                break;
            case GridFightSessionPhase.AwaitingInitialOrb:
            case GridFightSessionPhase.AwaitingInitialReturn:
            case GridFightSessionPhase.AwaitingBattleReturn:
                pending.ReturnPreparationAction = new GridFightReturnPreparationActionInfo();
                break;
            default:
                return null;
        }
        return pending;
    }

    public GridFightSyncUpdateResultScNotify BuildSync(
        GridFightUpdateSrcType source,
        GridFightSessionChange? change = null,
        bool includeGold = false,
        bool includeShop = false,
        bool includeLevel = false,
        bool levelChanged = false,
        bool includePending = false,
        bool includeRoute = false)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;
        var result = new GridFightSyncResultData { UpdateSource = source };
        if (includeGold) result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (levelChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
        if (includeShop) result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        if (includeLevel)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                PlayerLevel = new GridFightPlayerLevelSyncInfo
                {
                    Level = session.PlayerLevel,
                    Exp = session.PlayerExp,
                    MaxLevel = Catalog.MaxPlayerLevel,
                },
            });
        if (includePending && BuildPendingAction() is { } pending)
            result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        if (includeRoute)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                LevelInfo = new GridFightLevelSyncInfo
                {
                    ChapterId = session.ChapterId,
                    SectionId = session.SectionId,
                    LayerInfo = BuildLayerInfo(),
                },
            });
        if (change != null) AppendSessionChanges(result, change);
        notify.ResultList.Add(result);
        AppendMarketVitalitySavedValueResults(notify);
        AppendEconomyEffects(notify);
        AppendOwlbertArrivals(notify);
        AppendRoleDerivedStateResult(notify, source);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildGoldSync()
    {
        return BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, includeGold: true);
    }

    public GridFightSyncUpdateResultScNotify BuildRoleAddedSync(GridFightSessionChange change)
    {
        return BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, change);
    }

    public GridFightSyncUpdateResultScNotify BuildRoleAcquisitionSync(
        GridFightRoleAcquisitionResult acquisition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !acquisition.Success) return notify;
        var result = new GridFightSyncResultData();
        AppendAcquiredRoles(result, acquisition);
        notify.ResultList.Add(result);
        AppendRoleMergeResults(notify, GridFightUpdateSrcType.KgridFightSrcNone, [], acquisition);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcNone);
        AppendUnlockEffects(notify, acquisition);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildEquipmentAddedSync(GridFightEquipmentState equipment)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var items = new GridFightGameItemSyncInfo();
        items.GridFightEquipmentList.Add(BuildEquipment(equipment));
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        AppendPendingTrackedAutoEquipRoles(result);
        notify.ResultList.Add(result);
        AppendDiceEquipmentRefreshSync(notify);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildForgeUseSync(GridFightForgeUseResult useResult)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !useResult.Success || useResult.Equipment == null) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcUseForge,
        };
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            RemoveForgeUniqueId = useResult.RemovedForgeUniqueId,
        });
        var items = new GridFightGameItemSyncInfo();
        items.GridFightEquipmentList.Add(BuildEquipment(useResult.Equipment));
        result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        AppendPendingTrackedAutoEquipRoles(result);
        AppendPositionChanges(result, useResult.UpdatedRoles, useResult.UpdatedForgeItems);
        notify.ResultList.Add(result);
        AppendDiceEquipmentRefreshSync(notify);
        AppendOotdRefreshResult(notify);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildOrbAddedSync(GridFightOrbState orb)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            OrbInfo = new GridFightOrbSyncInfo
            {
                UniqueId = orb.UniqueId,
                OrbItemId = orb.OrbItemId,
            },
        });
        notify.ResultList.Add(result);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildConsumableSync(GridFightConsumableChange change)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var items = new GridFightGameItemSyncInfo();
        items.UpdateGridFightConsumableList.Add(BuildConsumableUpdate(change));
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(change.IsNew
            ? new GridFightSyncData { AddGameItemInfo = items }
            : new GridFightSyncData { UpdateGameItemInfo = items });
        notify.ResultList.Add(result);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildSectionMoveSync(uint finishedPendingPosition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var finishResult = new GridFightSyncResultData();
        if (finishedPendingPosition != 0)
            finishResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                FinishPendingActionPos = finishedPendingPosition,
            });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);

        var section = BuildSync(
            GridFightUpdateSrcType.KgridFightSrcEnterNode,
            includeShop: true,
            includeRoute: true);
        notify.ResultList.Add(section.ResultList);
        if (BuildPendingAction() is { } pending)
        {
            var pendingResult = new GridFightSyncResultData();
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                LockInfo = BuildPendingLockInfo(),
            });
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(pendingResult);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildBuyGoodsSync(
        IReadOnlyList<uint> goodsIndices,
        GridFightRoleAcquisitionResult acquisition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null || !acquisition.Success) return notify;

        var purchaseResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcBuyGoods,
        };
        purchaseResult.EffectParamList.Add(goodsIndices);
        purchaseResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        AppendAcquiredRoles(purchaseResult, acquisition);
        notify.ResultList.Add(purchaseResult);
        AppendRoleMergeResults(
            notify,
            GridFightUpdateSrcType.KgridFightSrcBuyGoods,
            goodsIndices,
            acquisition);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcBuyGoods);
        AppendUnlockEffects(notify, acquisition);
        AppendTwinPrimesResults(notify);
        AppendEconomyEffects(notify);

        var shopResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcBuyGoods,
        };
        shopResult.EffectParamList.Add(goodsIndices);
        shopResult.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(shopResult);
        AppendOotdRefreshResult(notify);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildRefreshShopSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;

        var refreshResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcManualRefreshGoods,
        };
        refreshResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        refreshResult.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(refreshResult);
        AppendAugmentEffectResults(notify, session.TakePortalRefreshEffects());
        AppendEconomyEffects(notify);
        refreshResult.UpdateDynamicList.Add(new GridFightSyncData { PlayerLevel = new GridFightPlayerLevelSyncInfo
        {
            Level = session.PlayerLevel, Exp = session.PlayerExp, MaxLevel = Catalog.MaxPlayerLevel,
        } });
        refreshResult.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
        AppendProcurementRefreshSync(notify);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildSpecialGoodsSync(
        IReadOnlyList<GridFightSpecialGoodsPurchaseResult> purchases)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || purchases.Count == 0) return notify;
        foreach (var purchase in purchases)
        {
            var result = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcSpecialGoods,
            };
            result.EffectParamList.Add(purchase.SpecialGoodsId);
            result.EffectParamList.Add(purchase.GoodsIndex);
            AppendRewardApplication(result, purchase.Application);
            AppendLeadingAcquiredRoles(result, purchase.Application.RoleAcquisitions);
            notify.ResultList.Add(result);
            AppendRoleAcquisitionMerges(
                notify,
                GridFightUpdateSrcType.KgridFightSrcSpecialGoods,
                [purchase.SpecialGoodsId, purchase.GoodsIndex],
                purchase.Application.RoleAcquisitions);
        }

        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcSpecialGoods);
        foreach (var acquisition in purchases.SelectMany(purchase => purchase.Application.RoleAcquisitions))
            AppendUnlockEffects(notify, acquisition);
        var shopResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcBuyGoods,
        };
        shopResult.EffectParamList.Add(purchases.Select(purchase => purchase.GoodsIndex));
        shopResult.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(shopResult);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildOrbOpenSync(GridFightRewardApplication application)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !application.Success) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcUseOrb,
        };
        result.EffectParamList.Add(application.SourceUniqueId);
        result.EffectParamList.Add(application.SourceItemId);
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            RemoveOrbUniqueId = application.SourceUniqueId,
        });
        AppendRewardApplication(result, application);
        AppendLeadingAcquiredRoles(result, application.RoleAcquisitions);
        notify.ResultList.Add(result);
        AppendRoleAcquisitionMerges(
            notify,
            GridFightUpdateSrcType.KgridFightSrcUseOrb,
            [application.SourceUniqueId, application.SourceItemId],
            application.RoleAcquisitions);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcUseOrb);
        foreach (var acquisition in application.RoleAcquisitions)
            AppendUnlockEffects(notify, acquisition);
        return notify;
    }

    public GridFightOpenOrbResultScNotify BuildOpenOrbResult(GridFightRewardApplication application)
    {
        var result = new GridFightOpenOrbResultScNotify
        {
            UniqueId = application.SourceUniqueId,
            DropInfo = new GridFightDropInfo(),
        };
        result.DropInfo.DropList.Add(application.Drops.Select(BuildDropEntry));
        return result;
    }

    public GridFightSyncUpdateResultScNotify BuildHeadPlayerEnhanceSync(
        GridFightHeadPlayerEnhanceResult enhancement)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null || !enhancement.Success) return notify;

        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectSelectEnhance,
        };
        result.EffectParamList.Add(enhancement.OptionId);

        foreach (var uniqueId in enhancement.RemovedRoleIds)
            result.UpdateDynamicList.Add(new GridFightSyncData { RemoveRoleUniqueId = uniqueId });
        if (enhancement.GoldChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (enhancement.UpdatedRole != null)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(enhancement.UpdatedRole) });
        if (enhancement.Equipment != null)
        {
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(BuildEquipment(enhancement.Equipment));
            result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        }
        if (enhancement.IsFinalHack && session.HackConsole != null)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                GameDataItemInfo = BuildHackConsoleItem(session.HackConsole),
            });
        if (enhancement.ShopChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(result);

        if (enhancement.UpdatedRole != null)
            AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcTraitEffectSelectEnhance);

        AppendJoyContractHeadPlayerReward(notify, enhancement);

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = enhancement.FinishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);
        AppendNextTraitEnhancement(notify);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildHackOptionSync(GridFightHackOptionResult option)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session?.HackConsole == null || !option.Success) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectSelectEnhance,
        };
        result.EffectParamList.Add(GridFightSpecialFeatures.SilverWolfEnhanceOptionId);
        result.EffectParamList.Add(Session.HackConsole.GameItemUniqueId);
        AppendRewardApplication(result, option.Application);
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            GameDataItemInfo = BuildHackConsoleItem(Session.HackConsole),
        });
        notify.ResultList.Add(result);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildPreparationSync()
    {
        var notify = BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, includeGold: true, includePending: true);
        AppendNextTraitEnhancement(notify);
        if (Session?.HackConsole is not { } hackConsole) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectSelectEnhance,
        };
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            GameDataItemInfo = BuildHackConsoleItem(hackConsole),
        });
        notify.ResultList.Add(result);
        return notify;
    }

    private void AppendRewardApplication(
        GridFightSyncResultData result,
        GridFightRewardApplication application)
    {
        var session = Session!;
        if (application.GoldChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (application.PlayerLevelChanged || application.PlayerExpChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                PlayerLevel = new GridFightPlayerLevelSyncInfo
                {
                    Level = session.PlayerLevel,
                    Exp = session.PlayerExp,
                    MaxLevel = Catalog.MaxPlayerLevel,
                },
            });
        // A Wealth Diamond (财富宝钻) raises the team size (1583 -> GridFightPlayer.UpdateMaxRoleNum) and unlocks one more
        // back cell (833 -> UpdateMaxBackRoleNum); without both the client keeps the old cap until a level-up and never
        // shows the cell.
        var addsCrown = application.AddedEquipments.Any(equipment =>
            equipment.EquipmentId == GridFightSession.WealthDiamondEquipmentId);
        if (application.PlayerLevelChanged || addsCrown)
            result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
        if (addsCrown)
            result.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = session.OffFieldMaxCount });
        if (application.LineupHpChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                LineupHp = new GridFightLineupHpSyncInfo
                {
                    CurrentHp = session.LineupHp,
                    MaxHp = session.MaxLineupHp,
                },
            });
        if (application.AddedEquipments.Count > 0)
        {
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(application.AddedEquipments.Select(BuildEquipment));
            result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        }
        foreach (var change in application.ConsumableChanges)
        {
            var items = new GridFightGameItemSyncInfo();
            items.UpdateGridFightConsumableList.Add(BuildConsumableUpdate(change));
            result.UpdateDynamicList.Add(change.IsNew
                ? new GridFightSyncData { AddGameItemInfo = items }
                : new GridFightSyncData { UpdateGameItemInfo = items });
        }
        foreach (var orb in application.AddedOrbs)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo
                {
                    UniqueId = orb.UniqueId,
                    OrbItemId = orb.OrbItemId,
                },
            });
        foreach (var forgeItem in application.AddedForgeItems)
            result.UpdateDynamicList.Add(new GridFightSyncData { AddForgeItemInfo = BuildForgeItem(forgeItem) });
        if (application.RolePropertiesChanged)
        {
            // Roles this reward created reach the client later (AddRoleInfo, merge steps) with their current state.
            var notYetSent = application.RoleAcquisitions
                .SelectMany(acquisition => acquisition.AcquiredRoles.Concat(acquisition.MergeSteps.Select(step => step.AddedRole)))
                .Select(role => role.UniqueId)
                .ToHashSet();
            foreach (var role in session.Roles.Where(role => !notYetSent.Contains(role.UniqueId)))
                result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        }
        AppendPendingTrackedAutoEquipRoles(result);
    }

    private static GridFightDropEntry BuildDropEntry(GridFightRewardEntry reward)
    {
        var entry = new GridFightDropEntry
        {
            DropType = reward.Type switch
            {
                Enums.GridFight.GridFightBonusTypeEnum.Gold => GridFightDropType.Coin,
                Enums.GridFight.GridFightBonusTypeEnum.Exp => GridFightDropType.Exp,
                Enums.GridFight.GridFightBonusTypeEnum.SpecificAvatar => GridFightDropType.Role,
                Enums.GridFight.GridFightBonusTypeEnum.Item => GridFightDropType.Item,
                Enums.GridFight.GridFightBonusTypeEnum.Orb => GridFightDropType.Orb,
                _ => GridFightDropType.None,
            },
            ItemId = reward.ItemId,
            Num = reward.Count,
        };
        if (reward.RoleStar != 0)
            entry.Param = new GridFightDropParam { RoleStar = reward.RoleStar };
        return entry;
    }

    private void AppendSessionChanges(GridFightSyncResultData result, GridFightSessionChange change)
    {
        foreach (var role in change.AddedRoles)
            result.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(role) });
        foreach (var uniqueId in change.RemovedRoleIds)
            result.UpdateDynamicList.Add(new GridFightSyncData { RemoveRoleUniqueId = uniqueId });
        AppendPositionChanges(result, change.UpdatedRoles, change.UpdatedForgeItems);
    }

    private GridFightRoleSwitchInfo BuildRoleSwitchInfo()
    {
        var info = new GridFightRoleSwitchInfo();
        if (Session == null) return info;
        info.RoleSwitchList.Add(Session.RoleSwitchEntries.Select(entry =>
        {
            var result = new GridFightRoleSwitchEntry
            {
                CurrentRoleId = entry.CurrentRoleId,
            };
            result.RoleIdList.Add(entry.RoleIdList);
            return result;
        }));
        return info;
    }

    private void AppendRoleDerivedStateResult(
        GridFightSyncUpdateResultScNotify notify,
        GridFightUpdateSrcType source,
        bool includeShop = false)
    {
        AppendDiceEquipmentRefreshSync(notify);
        var session = Session;
        var update = session?.ConsumeRoleSwitchUpdate();
        if (session == null) return;
        var savedValueChanges = session.RefreshRoleSavedValues();
        if (update == null && savedValueChanges.Count == 0) return;

        var result = new GridFightSyncResultData { UpdateSource = source };
        if (update != null)
        {
            result.UpdateDynamicList.Add(new GridFightSyncData { RoleSwitchInfo = BuildRoleSwitchInfo() });
            foreach (var uniqueId in update.ChangedRoleUniqueIds)
            {
                var role = session.Roles.FirstOrDefault(candidate => candidate.UniqueId == uniqueId);
                if (role != null)
                    result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
            }
        }
        AppendRoleSavedValueUpdates(result, savedValueChanges);
        if (includeShop && update != null)
            result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(result);
    }

    private static void AppendRoleSavedValueUpdates(
        GridFightSyncResultData result,
        IReadOnlyDictionary<string, uint> changes)
    {
        foreach (var (key, value) in changes)
        {
            var update = new GridFightSavedValueUpdate();
            update.SavedValues[key] = value;
            result.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = update });
        }
    }

    private void AppendPositionChanges(
        GridFightSyncResultData result,
        IEnumerable<GridFightRoleState> roles,
        IEnumerable<GridFightForgeItemState> forgeItems)
    {
        foreach (var role in roles)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        foreach (var forgeItem in forgeItems)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateForgeItemInfo = BuildForgeItem(forgeItem) });
    }

    private void AppendAcquiredRoles(
        GridFightSyncResultData result,
        GridFightRoleAcquisitionResult acquisition)
    {
        foreach (var role in acquisition.AcquiredRoles)
            result.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(role) });
    }

    /// <summary>
    ///     Number of acquisitions whose new roles can share the leading result: everything up to and including the
    ///     first one that merged. A role granted after a merge may sit in the bench cell that merge just emptied, so
    ///     the client must get it after that merge (see <see cref="AppendRoleAcquisitionMerges" />); otherwise it
    ///     lands on a cell that is still taken there, the cell looks empty and every move into it is refused.
    /// </summary>
    private static int CountLeadingAcquisitions(IReadOnlyList<GridFightRoleAcquisitionResult> acquisitions)
    {
        var index = 0;
        while (index < acquisitions.Count && acquisitions[index].MergeSteps.Count == 0) index++;
        return Math.Min(index + 1, acquisitions.Count);
    }

    /// <summary>The new roles of the leading acquisitions, for the result that carries the reward source.</summary>
    private void AppendLeadingAcquiredRoles(
        GridFightSyncResultData result,
        IReadOnlyList<GridFightRoleAcquisitionResult> acquisitions)
    {
        foreach (var acquisition in acquisitions.Take(CountLeadingAcquisitions(acquisitions)))
            AppendAcquiredRoles(result, acquisition);
    }

    /// <summary>
    ///     After the leading result: every acquisition's merges in the order the session applied them, each later
    ///     acquisition's new roles right before its own merges.
    /// </summary>
    private void AppendRoleAcquisitionMerges(
        GridFightSyncUpdateResultScNotify notify,
        GridFightUpdateSrcType acquisitionSource,
        IReadOnlyList<uint> effectParameters,
        IReadOnlyList<GridFightRoleAcquisitionResult> acquisitions,
        bool unlockEffects = false)
    {
        var leading = CountLeadingAcquisitions(acquisitions);
        for (var index = 0; index < acquisitions.Count; index++)
        {
            var acquisition = acquisitions[index];
            if (index >= leading && acquisition.AcquiredRoles.Count > 0)
            {
                var added = new GridFightSyncResultData();
                AppendAcquiredRoles(added, acquisition);
                notify.ResultList.Add(added);
            }
            AppendRoleMergeResults(notify, acquisitionSource, effectParameters, acquisition);
            if (unlockEffects) AppendUnlockEffects(notify, acquisition);
        }
    }

    private void AppendRoleMergeSteps(
        GridFightSyncUpdateResultScNotify notify,
        GridFightRoleAcquisitionResult acquisition)
    {
        foreach (var step in acquisition.MergeSteps)
        {
            var mergeResult = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcMergeRole,
            };
            foreach (var uniqueId in step.RemovedRoleIds)
                mergeResult.UpdateDynamicList.Add(new GridFightSyncData { RemoveRoleUniqueId = uniqueId });
            mergeResult.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(step.AddedRole) });
            notify.ResultList.Add(mergeResult);
        }
        AppendHeroEntranceDirtyResult(notify);
    }

    private void AppendRoleMergeResults(
        GridFightSyncUpdateResultScNotify notify,
        GridFightUpdateSrcType acquisitionSource,
        IEnumerable<uint> effectParameters,
        GridFightRoleAcquisitionResult acquisition)
    {
        AppendRoleMergeSteps(notify, acquisition);
        if (acquisition.FinalMergedRoles.Count == 0) return;
        var finalResult = new GridFightSyncResultData { UpdateSource = acquisitionSource };
        finalResult.EffectParamList.Add(effectParameters);
        foreach (var role in acquisition.FinalMergedRoles)
            finalResult.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        notify.ResultList.Add(finalResult);
    }

    private void AppendUnlockEffects(
        GridFightSyncUpdateResultScNotify notify,
        GridFightRoleAcquisitionResult acquisition)
    {
        foreach (var effect in acquisition.UnlockEffects)
        {
            switch (effect)
            {
                case GridFightRoleUnlockEffect.CyrenePoemShop:
                {
                    var result = new GridFightSyncResultData
                    {
                        UpdateSource = GridFightUpdateSrcType.KgridFightSrcSpecialGoods,
                    };
                    result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
                    notify.ResultList.Add(result);
                    break;
                }
                case GridFightRoleUnlockEffect.SilverWolfTraitActivation:
                    AppendSilverWolfTraitActivationResults(notify);
                    break;
                case GridFightRoleUnlockEffect.SilverWolfHeadPlayerPending:
                case GridFightRoleUnlockEffect.SilverWolfHackConsole:
                    AppendHeadPlayerPendingResult(notify);
                    break;
            }
        }
    }

    private static void AppendSilverWolfTraitActivationResults(GridFightSyncUpdateResultScNotify notify)
    {
        foreach (var effectId in new[]
                 {
                     GridFightSpecialFeatures.SilverWolfThreeCostTraitEffectId,
                     GridFightSpecialFeatures.SilverWolfFourCostTraitEffectId,
                     GridFightSpecialFeatures.SilverWolfFiveCostTraitEffectId,
                 })
        {
            var result = new GridFightSyncResultData();
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                TraitInfo = new GridFightTraitSyncInfo
                {
                    TraitId = GridFightSpecialFeatures.SilverWolfTraitId,
                    TraitEffectInfo = new GridFightTraitEffectInfo { EffectId = effectId },
                },
            });
            notify.ResultList.Add(result);
        }
    }

    private void AppendHeadPlayerPendingResult(GridFightSyncUpdateResultScNotify notify)
    {
        var headPlayer = Session?.HeadPlayerPending;
        if (headPlayer == null) return;

        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate,
        };
        result.EffectParamList.Add(GridFightSpecialFeatures.SilverWolfTraitId);
        result.EffectParamList.Add(headPlayer.EffectId);
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            LockInfo = BuildPendingLockInfo(),
        });
        if (BuildPendingAction() is { } pending)
            result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        notify.ResultList.Add(result);
    }

    public GridFightSyncUpdateResultScNotify BuildPortalSelectionSync(
        uint finishedQueuePosition,
        GridFightSessionChange change)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;

        AppendRotationHeroPortalInitialResults(notify);
        AppendInvitationPortalInitialResult(notify);
        AppendLaborDispatchPortalInitialResult(notify);
        AppendPepePortalInitialResults(notify);
        AppendJoyContractInitialResults(notify);
        AppendDotContractInitialResults(notify);
        AppendPortalInitialResults(notify);
        AppendAugmentEffectResults(notify, session.TakePortalOpeningEffects());

        var portalResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectPortalBuff,
        };
        portalResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            PortalUpdate = new GridFightPortalBuffUpdateInfo { PortalBuffId = session.SelectedPortalId },
        });
        notify.ResultList.Add(portalResult);

        var supplyResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcInitialSupplySelect,
        };
        supplyResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (session.ConceptStockInitialEquipment is { } conceptEquipment)
        {
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(BuildEquipment(conceptEquipment));
            supplyResult.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        }
        foreach (var role in change.AddedRoles.Where(role =>
                     role.UniqueId != session.JoyContractInitialRoleUniqueId &&
                     role.UniqueId != session.HeroEntranceRoleUniqueId))
            supplyResult.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(role) });
        notify.ResultList.Add(supplyResult);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcInitialSupplySelect);

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = finishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);

        var levelResult = new GridFightSyncResultData();
        levelResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LevelInfo = new GridFightLevelSyncInfo
            {
                ChapterId = session.ChapterId,
                SectionId = session.SectionId,
                LayerInfo = BuildLayerInfo(),
            },
        });
        notify.ResultList.Add(levelResult);

        var pendingResult = new GridFightSyncResultData();
        pendingResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LockInfo = BuildPendingLockInfo(),
        });
        if (BuildPendingAction() is { } pending)
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        notify.ResultList.Add(pendingResult);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildPendingAdvanceSync(uint finishedQueuePosition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = finishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);

        if (BuildPendingAction() is { } pending)
        {
            var pendingResult = new GridFightSyncResultData();
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                LockInfo = BuildPendingLockInfo(),
            });
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(pendingResult);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildInitialRoundSync(
        uint finishedQueuePosition,
        IReadOnlyList<GridFightTalentOrbReward> talentRewards,
        GridFightConsumableChange? initialConsumable)
    {
        var notify = new GridFightSyncUpdateResultScNotify();

        // Official order: Talent 1081/36 -> Orb 203, then 350101 with source value 21,
        // then the first shop, then pending-action advancement.
        AppendTalentOrbRewards(notify, talentRewards);
        if (initialConsumable != null)
        {
            var consumableResult = new GridFightSyncResultData
            {
                // Wire value confirmed by both official captures. The community enum label for 21
                // is not semantically reliable, therefore retain the official numeric value.
                UpdateSource = (GridFightUpdateSrcType)21,
            };
            consumableResult.EffectParamList.Add(new uint[] { 10101, 3 });
            var items = new GridFightGameItemSyncInfo();
            items.UpdateGridFightConsumableList.Add(BuildConsumableUpdate(initialConsumable));
            consumableResult.UpdateDynamicList.Add(initialConsumable.IsNew
                ? new GridFightSyncData { AddGameItemInfo = items }
                : new GridFightSyncData { UpdateGameItemInfo = items });
            notify.ResultList.Add(consumableResult);
        }

        AppendRotationHeroRoundBeginResult(notify);
        notify.ResultList.Add(BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, includeShop: true).ResultList);
        var pending = BuildPendingAdvanceSync(finishedQueuePosition);
        notify.ResultList.Add(pending.ResultList);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildBattleRoundSync(
        uint finishedQueuePosition,
        IReadOnlyList<GridFightTalentOrbReward> talentRewards)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        AppendLaborDispatchBossReward(notify);
        AppendGalaxyScholarRoundBeginResult(notify);
        AppendRoundBeginInvestmentRewards(notify);
        AppendPepeCameoResults(notify);
        AppendPepeDropResult(notify);
        AppendTalentOrbRewards(notify, talentRewards);
        AppendRotationHeroRoundBeginResult(notify);
        notify.ResultList.Add(BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, includeShop: true).ResultList);
        var pending = BuildPendingAdvanceSync(finishedQueuePosition);
        notify.ResultList.Add(pending.ResultList);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildEncounterRerollSync()
    {
        return BuildSync(
            GridFightUpdateSrcType.KgridFightSrcNone,
            includePending: true,
            includeRoute: true);
    }

    public GridFightSyncUpdateResultScNotify BuildEncounterSelectionSync(
        uint finishedQueuePosition,
        uint eliteBranchId)
    {
        var notify = BuildEncounterSelectionUpdateSync(eliteBranchId);
        notify.ResultList.Add(BuildPendingAdvanceSync(finishedQueuePosition).ResultList);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildEncounterSelectionUpdateSync(uint eliteBranchId)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var selection = new GridFightSyncResultData();
        selection.UpdateDynamicList.Add(new GridFightSyncData
        {
            EliteBranchSyncInfo = new GridFightEliteBranchSyncInfo { EliteBranchId = eliteBranchId },
        });
        notify.ResultList.Add(selection);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildBattleEndRouteSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var routeResult = new GridFightSyncResultData();
        routeResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LevelInfo = new GridFightLevelSyncInfo
            {
                ChapterId = Session.ChapterId,
                SectionId = Session.SectionId,
                LayerInfo = BuildLayerInfo(),
            },
        });
        notify.ResultList.Add(routeResult);
        if (BuildPendingAction() is { } pending)
        {
            var pendingResult = new GridFightSyncResultData();
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                LockInfo = BuildPendingLockInfo(),
            });
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(pendingResult);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildInvestmentSelectionSync(
        uint finishedQueuePosition,
        uint augmentId)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;

        foreach (var itemReward in Session.TakeInvestmentSelectionRewards())
            AppendInvestmentItemReward(notify, itemReward);
        AppendSpecialInvestmentSelectionResults(notify, augmentId);
        AppendInvestmentSelectionResults(notify, augmentId);
        AppendAugmentEffectResults(notify, Session.TakeAugmentSelectionEffects());
        AppendOwlbertArrivals(notify);
        AppendEconomyEffects(notify);
        if (augmentId is GridFightSession.PrismaticProcurementAugmentId or GridFightSession.GoldProcurementAugmentId)
            AppendProcurementRefreshSync(notify);

        if (Session.TakeGoldFuturesChildSelection(augmentId))
        {
            var selectionResult = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcAugment,
            };
            selectionResult.EffectParamList.Add(GridFightSession.GoldFuturesPlusAugmentId);
            selectionResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                AddAugmentInfo = new GridFightGameAugmentAdd
                {
                    UpdateAugmentInfo = new GridGameAugmentInfo
                    {
                        AugmentId = augmentId,
                        NEKJGIDPCAI = true,
                    },
                },
            });
            notify.ResultList.Add(selectionResult);
        }
        else
        {
            var selectionResult = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectAugment,
            };
            selectionResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                AddAugmentInfo = new GridFightGameAugmentAdd
                {
                    UpdateAugmentInfo = BuildInvestment(augmentId),
                },
            });
            notify.ResultList.Add(selectionResult);
        }
        foreach (var bonusId in Session.TakeForesightBonusAugments())
        {
            AppendSpecialInvestmentSelectionResults(notify, bonusId);
            AppendInvestmentSelectionResults(notify, bonusId);
            var bonusResult = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcAugment };
            bonusResult.EffectParamList.Add(augmentId);
            bonusResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                AddAugmentInfo = new GridFightGameAugmentAdd { UpdateAugmentInfo = BuildInvestment(bonusId) },
            });
            notify.ResultList.Add(bonusResult);
        }
        notify.ResultList.Add(BuildPendingAdvanceSync(finishedQueuePosition).ResultList);
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildSupplySelectionSync(
        uint finishedQueuePosition,
        uint selectedIndex,
        GridFightSupplySelectionResult selection)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !selection.Success || selection.Equipment == null) return notify;

        var selectionResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectSupply,
        };
        selectionResult.EffectParamList.Add(selectedIndex);
        var items = new GridFightGameItemSyncInfo();
        items.GridFightEquipmentList.Add(BuildEquipment(selection.Equipment));
        selectionResult.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        notify.ResultList.Add(selectionResult);

        var roleResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectSupply,
        };
        roleResult.EffectParamList.Add(selectedIndex);
        AppendAcquiredRoles(roleResult, selection.Acquisition);
        notify.ResultList.Add(roleResult);
        AppendRoleMergeResults(
            notify,
            GridFightUpdateSrcType.KgridFightSrcSelectSupply,
            [selectedIndex],
            selection.Acquisition);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcSelectSupply);
        AppendUnlockEffects(notify, selection.Acquisition);

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = finishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);

        var routeResult = new GridFightSyncResultData();
        routeResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LevelInfo = new GridFightLevelSyncInfo
            {
                ChapterId = Session.ChapterId,
                SectionId = Session.SectionId,
                LayerInfo = BuildLayerInfo(),
            },
        });
        notify.ResultList.Add(routeResult);

        if (BuildPendingAction() is { } pending)
        {
            var pendingResult = new GridFightSyncResultData();
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(pendingResult);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildPositionSync(GridFightSessionChange change)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !change.Success) return notify;
        var savedValueChanges = Session.RefreshRoleSavedValues();
        if (Session.ConsumeRoleSwitchUpdate() is { } update)
        {
            var previousResult = new GridFightSyncResultData();
            foreach (var role in change.UpdatedRoles)
            {
                var previousRoleId = update.PreviousRoleIds.GetValueOrDefault(role.UniqueId, role.RoleId);
                previousResult.UpdateDynamicList.Add(new GridFightSyncData
                {
                    UpdateRoleInfo = BuildRole(role, previousRoleId),
                });
            }
            foreach (var forgeItem in change.UpdatedForgeItems)
                previousResult.UpdateDynamicList.Add(new GridFightSyncData
                {
                    UpdateForgeItemInfo = BuildForgeItem(forgeItem),
                });
            notify.ResultList.Add(previousResult);

            var switchResult = new GridFightSyncResultData();
            switchResult.UpdateDynamicList.Add(new GridFightSyncData { RoleSwitchInfo = BuildRoleSwitchInfo() });
            foreach (var uniqueId in update.ChangedRoleUniqueIds)
            {
                var role = Session.Roles.FirstOrDefault(candidate => candidate.UniqueId == uniqueId);
                if (role != null)
                    switchResult.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
            }
            switchResult.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
            AppendRoleSavedValueUpdates(switchResult, savedValueChanges);
            notify.ResultList.Add(switchResult);
            AppendHeadPlayerPositionEffects(notify, change);
            AppendFateDivinerInitialActivationResults(notify);
            notify.ResultList.Add(new GridFightSyncResultData());
            return notify;
        }
        var result = new GridFightSyncResultData();
        AppendPositionChanges(result, change.UpdatedRoles, change.UpdatedForgeItems);
        AppendRoleSavedValueUpdates(result, savedValueChanges);
        notify.ResultList.Add(result);
        AppendHeadPlayerPositionEffects(notify, change);
        AppendFateDivinerInitialActivationResults(notify);
        return notify;
    }

    private void AppendHeadPlayerPositionEffects(
        GridFightSyncUpdateResultScNotify notify,
        GridFightSessionChange change)
    {
        if (change.SilverWolfTraitActivated)
            AppendSilverWolfTraitActivationResults(notify);
        if (change.HeadPlayerPendingStarted)
            AppendHeadPlayerPendingResult(notify);
    }

    public GridFightSyncUpdateResultScNotify BuildBattleEnterSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;

        // First Mortenax Blade activation is preceded by the official TraitEffectUpdate.
        if (Session.HasMortenaxBladeTraitEffectSync)
            notify.ResultList.Add(BuildMortenaxBladeTraitEffectResult().ResultList);

        // Official GridFight captures send a gameplay lock (Reserved1) immediately before
        // GridFightEnterBattleStageScRsp. This is not an EnterNode update: the client uses
        // the lock transition to enter the battle state machine. Omitting it leaves the
        // client in the board state even though a valid SceneBattleInfo follows.
        var battleLockResult = new GridFightSyncResultData();
        battleLockResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LockInfo = new GridFightLockInfo
            {
                LockType = GridFightLockType.Gameplay,
                LockReason = GridFightLockReason.Reserved1,
            },
        });
        notify.ResultList.Add(battleLockResult);
        return notify;
    }

    private GridFightSyncUpdateResultScNotify BuildMortenaxBladeTraitEffectResult()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;
        var effect = session.MortenaxBladeBattleEffects.FirstOrDefault(item => item.NeedsTraitEffectSync);
        if (effect == null) return notify;

        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate,
        };
        result.EffectParamList.Add(new[]
        {
            effect.EquipmentId,
            0u,
            effect.TemporaryInstanceId,
            session.MortenaxBladeTraitEffectParam,
        });
        foreach (var role in session.Roles
                     .Where(role => role.Position is >= 1 and <= 13)
                     .OrderBy(role => role.Position))
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        notify.ResultList.Add(result);
        return notify;
    }

    /// <summary>
    ///     QuitBattleCsReq cleared a GridFight battle without a PVEBattleResultCsReq. The battle was already sent
    ///     to the client together with the gameplay lock, so settle it like BATTLE_END_QUIT (a lost battle, with the
    ///     same packets as a retired PVEBattleResultCsReq). Returns false when the context is stale or already settled.
    /// </summary>
    public async ValueTask<bool> QuitBattle(GridFightSession context)
    {
        var session = Session;
        if (session == null || !ReferenceEquals(session, context) || session.Phase != GridFightSessionPhase.InBattle)
            return false;
        await SettleBattle(session, null, false, GridFightBattleStatistics.Empty, retreated: true);
        Persist();
        return true;
    }

    private async ValueTask SendFailedBattleCleanup(GridFightSession session)
    {
        // Every official GridFight battle releases the gameplay lock when leaving battle,
        // including an abnormal/failed settlement. Keep the Mortenax trait cleanup behind it.
        var cleanup = new GridFightSyncUpdateResultScNotify();
        cleanup.ResultList.Add(BuildBattleUnlockResult());
        var completeMortenaxTraitLifecycle = session.HasMortenaxBladeTraitEffectSync;
        if (completeMortenaxTraitLifecycle)
            cleanup.ResultList.Add(BuildMortenaxBladeTraitEffectResult().ResultList);
        await Player.SendPacket(new PacketGridFightSyncUpdateResultScNotify(cleanup));
        if (completeMortenaxTraitLifecycle)
            session.CompleteMortenaxBladeTraitEffectSync();
        session.ClearMortenaxBladeBattleEffects();
    }

    public async ValueTask EndBattle(BattleInstance battle, PVEBattleResultCsReq request)
    {
        var session = Session;
        if (session == null) return;
        var reported = request.Stt?.GridFightStatistics?.ODMCMPIDJNH.ToList() ?? [];
        // No capture shows this list yet; log it so the next real one can confirm what it carries.
        if (reported.Count > 0)
            Logger.Debug($"GridFight battle of uid {Player.Uid} reported in-battle drops: " +
                         string.Join(", ", reported.Select(drop => $"{drop.DropType}:{drop.ItemId}x{drop.Num}")));
        var reportedOrbs = reported.Where(drop => drop.DropType == GridFightDropType.Orb)
            .Select(drop => new GridFightRewardEntry(Enums.GridFight.GridFightBonusTypeEnum.Orb, drop.ItemId, drop.Num))
            .ToList();
        await SettleBattle(session, battle, request.EndStatus == BattleEndStatus.BattleEndWin, ReadBattleStatistics(request),
            retreated: request.EndStatus == BattleEndStatus.BattleEndQuit, reportedDrops: reportedOrbs);
    }

    private async ValueTask SettleBattle(
        GridFightSession session,
        BattleInstance? battle,
        bool battleEndedNormally,
        GridFightBattleStatistics statistics,
        bool retreated = false,
        IReadOnlyList<GridFightRewardEntry>? reportedDrops = null)
    {
        var chapterId = session.ChapterId;
        var sectionId = session.SectionId;
        var previousLevel = session.PlayerLevel;
        var previousExp = session.PlayerExp;
        var previousMaxExp = Catalog.GetPlayerLevel(previousLevel).LevelUpExp;
        var settlement = session.ResolveBattle(battleEndedNormally, statistics, reportedDrops);
        if (!settlement.Success)
        {
            await SendFailedBattleCleanup(session);
            return;
        }
        if (retreated)
        {
            // Retreating (撤退) leaves Currency Wars altogether: right after BATTLE_END_QUIT the client sends
            // GridFightQuitLeaveGamePlayCsReq and walks back to the overworld (2026-10-06 log), and it reloads the run
            // through GetData when it comes back. The battle still counts as lost (no free replay), but its settle
            // page, the route sync and the settle notify must not reach a client that is leaving: they blacked it out.
            _lastBattleDamageStatistics = new GridFightDamageSttInfo();
            await SendFailedBattleCleanup(session);
            if (settlement.Defeated || !settlement.RouteAdvanced)
            {
                ClearSession(settled: true);
                await RecordGameFinished(session);
            }
            return;
        }
        // Without a battle instance (QuitBattleCsReq) there are no uploaded statistics to break down.
        var damageStatistics = battle == null
            ? new GridFightDamageSttInfo()
            : BuildDamageStatistics(battle, settlement.Statistics);
        _lastBattleDamageStatistics = damageStatistics.Clone();

        var settlementSync = BuildBattleSettlementSync(
            settlement,
            damageStatistics,
            chapterId,
            sectionId,
            session.PlayerLevel != previousLevel);
        var completeMortenaxTraitLifecycle = false;
        if (session.HasMortenaxBladeTraitEffectSync)
        {
            // Official first-battle order: clear the battle lock, repeat the TraitEffectUpdate with
            // the same parameters used at battle entry, then append ordinary BattleEnd results.
            // BuildBattleSettlementSync already starts with that mandatory unlock result.
            var combined = new GridFightSyncUpdateResultScNotify();
            combined.ResultList.Add(settlementSync.ResultList[0]);
            combined.ResultList.Add(BuildMortenaxBladeTraitEffectResult().ResultList);
            combined.ResultList.Add(settlementSync.ResultList.Skip(1));
            settlementSync = combined;
            completeMortenaxTraitLifecycle = true;
        }
        try
        {
            await Player.SendPacket(new PacketGridFightSyncUpdateResultScNotify(settlementSync));
            if (completeMortenaxTraitLifecycle)
                session.CompleteMortenaxBladeTraitEffectSync();
        }
        finally
        {
            // Temporary equipment exists only in the battle snapshot.
            session.ClearMortenaxBladeBattleEffects();
        }
        var endNotify = new GridFightEndBattleStageNotify
        {
            IsPerfectFinish = settlement.IsPerfectFinish,
            SectionId = sectionId,
            BasicGold = settlement.BasicGold,
            InterestGold = settlement.InterestGold,
            KeepWinGold = settlement.KeepWinGold,
            PreviousKeepWinCount = settlement.PreviousKeepWinCount,
            KeepWinCount = settlement.KeepWinCount,
            ProgressPercent = settlement.ProgressPercent,
            DeadLinePercent = settlement.DeadLinePercent,
            LineupHpBefore = settlement.LineupHpBefore,
            LineupHpAfter = settlement.LineupHpAfter,
            MaxLineupHp = session.MaxLineupHp,
            ChapterId = chapterId,
            RouteId = Catalog.StandardRouteId,
            DamageStatistics = damageStatistics,
            LevelUpdateInfo = new GridFightLevelUpdateInfo
            {
                AddedExp = settlement.AddedExp,
                PreviousLevelInfo = new GridFightLevelDisplayInfo
                {
                    Level = previousLevel,
                    Exp = previousExp,
                    MaxExp = previousMaxExp,
                },
                CurrentLevelInfo = new GridFightLevelDisplayInfo
                {
                    Level = session.PlayerLevel,
                    Exp = session.PlayerExp,
                    MaxExp = Catalog.GetPlayerLevel(session.PlayerLevel).LevelUpExp,
                },
            },
        };
        endNotify.HpModifyList.Add(settlement.HpChanges.Select(BuildHpModifyInfo));
        await Player.SendPacket(new PacketGridFightEndBattleStageNotify(endNotify));
        if (settlement.Defeated)
        {
            // Squad HP reached 0: the run is over (same packet order as the victory settle).
            var settle = BuildSettleNotify(GridFightSettleReason.Defeat);
            await Player.SendPacket(new PacketGridFightSyncKeepWinCntNotify(session.KeepWinCount));
            await Player.SendPacket(new PacketGridFightSettleNotify(settle));
            ClearSession(settled: true);
            await RecordGameFinished(session);
        }
        else if (settlement.RouteAdvanced)
            await Player.SendPacket(new PacketGridFightSyncUpdateResultScNotify(BuildBattleEndRouteSync()));
        else
        {
            var settle = BuildSettleNotify(GridFightSettleReason.Victory);
            await Player.SendPacket(new PacketGridFightSyncKeepWinCntNotify(1));
            await Player.SendPacket(new PacketGridFightSettleNotify(settle));
            var finished = Session!;
            ClearSession(settled: true);
            await RecordGameFinished(finished);
        }
    }

    private static GridFightSyncResultData BuildBattleUnlockResult()
    {
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        return result;
    }

    private GridFightSyncUpdateResultScNotify BuildBattleSettlementSync(
        GridFightBattleSettlementResult settlement,
        GridFightDamageSttInfo damageStatistics,
        uint chapterId,
        uint sectionId,
        bool levelChanged)
    {
        var session = Session!;
        var notify = new GridFightSyncUpdateResultScNotify();
        // Captures consistently begin the post-battle sync with an empty LockInfo, which
        // releases the Reserved1/Gameplay lock sent immediately before battle entry.
        notify.ResultList.Add(BuildBattleUnlockResult());
        var battleResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcBattleEnd,
        };
        battleResult.EffectParamList.Add(new[] { chapterId, sectionId });
        battleResult.UpdateDynamicList.Add(new GridFightSyncData { DamageStat = damageStatistics });
        battleResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LineupHp = new GridFightLineupHpSyncInfo
            {
                CurrentHp = settlement.LineupHpAfter,
                MaxHp = session.MaxLineupHp,
            },
        });
        battleResult.UpdateDynamicList.Add(new GridFightSyncData { KeepWinCount = settlement.KeepWinCount });
        notify.ResultList.Add(battleResult);

        var rewardResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcBattleEnd,
        };
        rewardResult.EffectParamList.Add(new[] { chapterId, sectionId });
        if (levelChanged)
        {
            rewardResult.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
            // Official battle-end level-ups immediately refresh the displayed rarity weights.
            // This is also required by portal 114 so its selected doubled tier is re-normalized
            // against the newly unlocked base shop probabilities without rerandomizing the tier.
            rewardResult.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        }
        rewardResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            PlayerLevel = new GridFightPlayerLevelSyncInfo
            {
                Level = session.PlayerLevel,
                Exp = session.PlayerExp,
                MaxLevel = Catalog.MaxPlayerLevel,
            },
        });
        rewardResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = settlement.GoldAfterBasic });
        if (settlement.InterestGold > 0)
            rewardResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = settlement.GoldAfterInterest });
        if (settlement.KeepWinGold > 0)
            rewardResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = settlement.GoldAfterKeepWin });
        foreach (var orb in settlement.AddedBattleOrbs)
            rewardResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo
                {
                    UniqueId = orb.UniqueId,
                    OrbItemId = orb.OrbItemId,
                },
            });
        notify.ResultList.Add(rewardResult);
        AppendMarketVitalitySavedValueResults(notify);
        AppendTalentOrbRewards(notify, settlement.TalentRewards);
        AppendDotContractBattleResults(notify);
        AppendDiceEquipmentRefreshSync(notify);
        return notify;
    }

    private static void AppendTalentOrbRewards(
        GridFightSyncUpdateResultScNotify notify,
        IReadOnlyList<GridFightTalentOrbReward> rewards)
    {
        foreach (var reward in rewards)
        {
            var result = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcTalent,
            };
            result.EffectParamList.Add(new[] { reward.TalentId, reward.EffectId });
            foreach (var orb in reward.AddedOrbs)
                result.UpdateDynamicList.Add(new GridFightSyncData
                {
                    OrbInfo = new GridFightOrbSyncInfo
                    {
                        UniqueId = orb.UniqueId,
                        OrbItemId = orb.OrbItemId,
                    },
                });
            notify.ResultList.Add(result);
        }
    }

    private static GridFightBattleStatistics ReadBattleStatistics(PVEBattleResultCsReq request)
    {
        var roundCount = request.Stt?.RoundCnt ?? 0;
        var statistics = request.Stt?.GridFightStatistics;
        if (statistics == null) return GridFightBattleStatistics.Empty with { RoundCount = roundCount };
        return new GridFightBattleStatistics(
            Math.Min(statistics.ProgressPercent, 100u),
            statistics.RoleStatistics.Select(row => new GridFightRoleBattleDamage(
                row.RoleId,
                NormalizeDamage(row.Damage),
                NormalizeDamage(row.SecondaryDamage))).ToList(),
            statistics.TraitStatistics.Select(row => new GridFightTraitBattleDamage(
                row.TraitId,
                NormalizeDamage(row.Damage),
                NormalizeDamage(row.SecondaryDamage))).ToList(),
            statistics.AugmentStatistics.Select(row => new GridFightAugmentBattleDamage(
                row.AugmentId,
                NormalizeDamage(row.Damage),
                NormalizeDamage(row.SecondaryDamage))).ToList(),
            statistics.WaveKilledMonsterInfoList.SelectMany(wave =>
                wave.KillMonsterList.Select(monster => new GridFightKilledMonsterInstance(
                    wave.WaveIndex,
                    monster.MonsterIndex,
                    monster.MonsterId))).ToList(),
            roundCount,
            statistics.POCKGKDFLKK.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            true);
    }

    private GridFightDamageSttInfo BuildDamageStatistics(BattleInstance battle, GridFightBattleStatistics statistics)
    {
        var session = Session!;
        var result = new GridFightDamageSttInfo();
        if (!statistics.IsPresent) return result;
        var uploadedRoleDamage = statistics.RoleDamage
            .GroupBy(row => row.RoleId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Damage));
        var activeRoles = session.Roles
            .Where(role => role.Position is >= 1 and <= 13)
            .GroupBy(role => role.RoleId)
            .Select(group => group.OrderBy(role => role.Position).First())
            .OrderBy(role => role.Position)
            .ToList();
        foreach (var role in activeRoles)
        {
            var isForeground = role.Position is >= 1 and <= 4;
            var isTrialRole = GridFightBattleProtoBuilder.IsTrialRole(battle, role.RoleId, isForeground);
            var row = new GridFightRoleDamageStatistics
            {
                RoleId = role.RoleId,
                RoleStar = role.Star,
                TotalDamage = uploadedRoleDamage.GetValueOrDefault(role.RoleId),
                IsTrialRole = isTrialRole,
                IsBackPosition = !isTrialRole && role.Position is >= 5 and <= 13,
            };
            row.GridFightEquipmentList.Add(session.GetEquippedEquipment(role)
                .Select(item => item.EquipmentId)
                .Distinct()
                .Order());
            result.RoleStatistics.Add(row);
        }
        foreach (var missing in uploadedRoleDamage.Where(pair => activeRoles.All(role => role.RoleId != pair.Key)))
            result.RoleStatistics.Add(new GridFightRoleDamageStatistics
            {
                RoleId = missing.Key,
                RoleStar = 1,
                TotalDamage = missing.Value,
            });

        foreach (var group in statistics.TraitDamage.GroupBy(row => row.TraitId))
        {
            var memberCount = (uint)activeRoles.Count(role => session.ResolveRoleTraitIds(role).Contains(group.Key)) +
                              (session.ResolvePepeTraitMembers().Any(entry => entry.TraitId == group.Key) ? 1u : 0u) +
                              session.ResolveTraitExtraMemberCount(group.Key, activeRoles);
            result.TraitStatistics.Add(new GridFightTraitDamageStatistics
            {
                TraitId = group.Key,
                Layer = GridFightBattleProtoBuilder.ResolveTraitLayer(group.Key, memberCount),
                Damage = group.Sum(row => row.Damage),
            });
        }
        foreach (var group in statistics.AugmentDamage.GroupBy(row => row.AugmentId))
        {
            var damage = group.Sum(row => row.Damage);
            var secondaryDamage = group.Sum(row => row.SecondaryDamage);
            if (damage == 0 && secondaryDamage == 0) continue;
            result.AugmentStatistics.Add(new GridFightAugmentDamageStatistics
            {
                AugmentId = group.Key,
                Damage = damage,
                SecondaryDamage = secondaryDamage,
            });
        }
        return result;
    }

    private static GridFightHpModifyInfo BuildHpModifyInfo(GridFightHpChange change)
    {
        return new GridFightHpModifyInfo
        {
            PreviousHp = checked((int)change.PreviousHp),
            CurrentHp = checked((int)change.CurrentHp),
            ChangeValue = change.Amount,
            SourceId = change.SourceId,
            Reason = change.Kind switch
            {
                GridFightHpChangeKind.BasicPenalty => GridFightUpdateGlobalHpReason.KsubGlobalHpBaseCost,
                GridFightHpChangeKind.ProgressPenalty => GridFightUpdateGlobalHpReason.KsubGlobalHpExtraCost,
                GridFightHpChangeKind.ThresholdPenalty => GridFightUpdateGlobalHpReason.KsubGlobalHpThresholdCost,
                GridFightHpChangeKind.TalentHealing => GridFightUpdateGlobalHpReason.KaddGlobalHpTalent,
                GridFightHpChangeKind.FinalBattleTurnHealing =>
                    GridFightUpdateGlobalHpReason.KaddGlobalHpFinalBattleTurn,
                _ => GridFightUpdateGlobalHpReason.KupdateGlobalHpNone,
            },
        };
    }

    private static double NormalizeDamage(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 0;
    }

    private GridFightGameInfo BuildBasicInfo(bool hasPendingAction)
    {
        var session = Session!;
        var lockInfo = new GridFightLockInfo();
        if (hasPendingAction)
        {
            lockInfo.LockType = GridFightLockType.Gameplay;
            lockInfo.LockReason = GridFightLockReason.PendingAction;
        }
        // Field meanings follow the 4.6.51 client (GridFightPlayer.SetData): 1 MaxHP, 3 PlayerLevelLimit, 4 MaxBackRoleNum,
        // 5 KeepWinCnt, 6 MaxBenchRoleNum, 7 Level, 11 RoleMaxNum. The client has no field for the interest deposit or
        // the bench overflow size.
        var basic = new GridFightGameBasicInfo
        {
            LineupHp = session.LineupHp,
            MaxLineupHp = session.MaxLineupHp,
            Gold = session.Gold,
            PlayerLevel = session.PlayerLevel,
            LevelExp = session.PlayerExp,
            MaxAvatarCount = Catalog.BenchSize,
            BuyExpCost = Catalog.BuyExpCost,
            MaxInterestGold = session.CurrentMaxInterestGold,
            OffFieldMaxCount = session.OffFieldMaxCount,
            MaxActiveRoleCount = session.MaxActiveRoles,
            MaxPlayerLevel = Catalog.MaxPlayerLevel,
            KeepWinCount = session.KeepWinCount,
            CurrentTaskInfo = new GridFightSyncCurrentTaskInfo(),
            LockInfo = lockInfo,
        };
        basic.ModuleIdList.Add(Catalog.ContentModuleId);
        return new GridFightGameInfo { BasicInfo = basic };
    }

    private GridFightGameShopInfo BuildShopInfo()
    {
        var session = Session!;
        var info = new GridFightGameShopInfo
        {
            RefreshCost = session.CurrentRefreshCost,
            RarityWeightInfo = BuildRarityWeights(),
        };
        info.RolePoolList.Add(Catalog.RolePool.Select(role => role.ID));
        info.GoodsList.Add(session.Shop.Select(BuildShopGoods));
        return info;
    }

    private GridFightGameItemsInfo BuildItemsInfo()
    {
        var session = Session!;
        var info = new GridFightGameItemsInfo();
        info.GridFightEquipmentList.Add(session.Equipments.Select(BuildEquipment));
        info.GridFightConsumableList.Add(session.Consumables.Select(consumable => new GridFightConsumableInfo
        {
            GroupId = consumable.GroupId,
            ItemId = consumable.ItemId,
            Num = consumable.Count,
        }));
        return info;
    }

    // The 4.7 client (GridFightConsumableInfo.SyncUpdate/SyncRemove) reads the item only from the nested
    // MCKHGIEJLHM (field 13) and throws when it is missing.
    private static GridFightConsumableUpdateInfo BuildConsumableUpdate(GridFightConsumableChange change)
    {
        return new GridFightConsumableUpdateInfo
        {
            GroupId = change.Consumable.GroupId,
            ItemId = change.Consumable.ItemId,
            ItemStackCount = checked((int)change.StackDelta),
            Num = change.Consumable.Count,
            MCKHGIEJLHM = new GridFightConsumableInfo
            {
                GroupId = change.Consumable.GroupId,
                ItemId = change.Consumable.ItemId,
                Num = change.Consumable.Count,
            },
        };
    }

    private GridFightGameOrbInfo BuildOrbInfo()
    {
        var info = new GridFightGameOrbInfo();
        info.GridGameOrbList.Add(Session!.Orbs.Select(orb => new GridGameOrbInfo
        {
            UniqueId = orb.UniqueId,
            OrbItemId = orb.OrbItemId,
        }));
        return info;
    }

    private static GridFightEquipmentInfo BuildEquipment(GridFightEquipmentState equipment)
    {
        return new GridFightEquipmentInfo
        {
            UniqueId = equipment.UniqueId,
            GridFightEquipmentId = equipment.EquipmentId,
            Source = equipment.Source,
            // Client GridFightEquipmentInfo.IsTemporary: no roll, "cannot operate temporary equipment".
            IODIJBEHEGF = equipment.IsRestricted,
        };
    }

    private static GridGameForgeItemInfo BuildForgeItem(GridFightForgeItemState forgeItem)
    {
        var info = new GridGameForgeItemInfo
        {
            UniqueId = forgeItem.UniqueId,
            ForgeItemId = forgeItem.ForgeItemId,
            Pos = forgeItem.Position,
        };
        info.ForgeGoodsList.Add(forgeItem.EquipmentChoices.Select(equipmentId =>
            new GridFightForgeGoodsInfo
            {
                EquipmentGoodsInfo = new GridFightForgeEquipmentInfo
                {
                    GridFightEquipmentId = equipmentId,
                },
            }));
        return info;
    }

    private static GridFightGameItemInfo BuildHackConsoleItem(GridFightHackConsoleState state)
    {
        var hackState = new GridFightHackStateInfo
        {
            MinimumEnemyHpPercent = GridFightSpecialFeatures.MinimumEnemyHpPercent,
            MaximumEnemyHpPercent = GridFightSpecialFeatures.MaximumEnemyHpPercent,
            EnemyHpPercent = state.EnemyHpPercent,
            EnhanceOptionId = GridFightSpecialFeatures.SilverWolfEnhanceOptionId,
        };
        hackState.RemainingActionCountList.Add(state.RemainingGoldActions);
        hackState.RemainingActionCountList.Add(state.RemainingHealActions);
        hackState.RemainingActionCountList.Add(state.RemainingOrbActions);
        return new GridFightGameItemInfo
        {
            UniqueId = state.GameItemUniqueId,
            Descriptor_ = BuildGameItemDescriptor(
                GridFightGameItemCategory.HackConsole,
                GridFightSpecialFeatures.SilverWolfEnhanceOptionId),
            HackStateInfo = hackState,
        };
    }

    private static GridGameAugmentInfo BuildInvestment(uint augmentId)
    {
        return new GridGameAugmentInfo
        {
            AugmentId = augmentId,
            IsActive = true,
        };
    }

    private GridFightShopSyncInfo BuildShopSyncInfo()
    {
        var session = Session!;
        var info = new GridFightShopSyncInfo
        {
            ABGEKPJHHIA = session.ShopLocked,
            RefreshCost = session.CurrentRefreshCost,
            RarityWeightInfo = BuildRarityWeights(),
        };
        info.GoodsList.Add(session.Shop.Select(BuildShopGoods));
        return info;
    }

    private GridFightRarityWeightInfo BuildRarityWeights()
    {
        var result = new GridFightRarityWeightInfo();
        var baseWeights = Catalog.GetShopRarityWeights(Session!.PlayerLevel);
        var weights = Session.GetEffectiveShopRarityWeights(baseWeights);
        for (var index = 0; index < weights.Count; index++)
            result.EntryList.Add(new GridFightRarityWeightEntry
            {
                Rarity = (uint)index + 1,
                Weight = weights[index],
            });
        return result;
    }

    private static GridFightShopGoodsInfo BuildShopGoods(GridFightShopSlot slot)
    {
        var goods = new GridFightShopGoodsInfo
        {
            SoldOut = slot.SoldOut,
            Price = slot.Price,
        };
        if (slot.SpecialGoodsId != 0)
            goods.SpecialGoodsInfo = new GridFightSpecialGoodsInfo
            {
                SpecialGoodsId = slot.SpecialGoodsId,
            };
        else
            goods.RoleGoodsInfo = new GridFightRoleGoodsInfo
            {
                RoleId = slot.RoleId,
                RoleStar = slot.Star,
            };
        return goods;
    }

    private GridFightLevelInfo BuildLevelInfo()
    {
        var session = Session!;
        var info = new GridFightLevelInfo
        {
            RouteId = Catalog.StandardRouteId,
            ChapterId = session.ChapterId,
            SectionId = session.SectionId,
            SectionProgressInfo = new GridFightSectionProgressInfo(),
            BossInfo = new GridFightBossInfo(),
            BattleStatisticsInfo = new GridFightBattleStatisticsInfo
            {
                DamageStatistics = _lastBattleDamageStatistics.Clone(),
            },
        };
        info.CampInfoList.Add(session.CampIds.Select(id => new GridFightCampInfo { CampId = id }));
        info.AffixInfoList.Add(session.AffixIds.Select(id => new GridFightAffixInfo { AffixId = id }));
        info.BossInfo.MonsterList.Add(session.Bosses.Select(monster => new GridFightMonsterInfo
        {
            MonsterId = monster.MonsterID,
            RoleStar = 1,
        }));
        info.OOMMIIKPHBC.Add(Catalog.StandardRoute.Select(node => BuildRouteNode(node, session)));
        return info;
    }

    // Since 4.6.51, GridFightStageRoute.SyncRouteNodes requires server nodes to show the stage and allow battle.
    // GridFightStageNode._RefreshCachedFields: params 2 stage, 7 penalty rule, 8 augment (=1), 12 gold, 14 type;
    // 4 spotlight and 11 bonfire are season-two only.
    private static BCCIJHPLJOJ BuildRouteNode(GridFightRouteNode node, GridFightSession session)
    {
        var param = new OHMDHKGAEMM
        {
            StageId = node.StageId,
            IFIIOIIKJHO = node.PenaltyBonusRuleId,
            PFEMDGNFHNH = node.Template.IsAugment,
            PICMDONCKCO = node.BasicGoldReward,
            NodeType = (uint)node.NodeType,
        };
        param.ParamList.Add(node.Parameters);
        // Spotlight (主角光环): param 4 marks the nodes whose spotlight selection is still due (and numbers the locked
        // slots); while the current node has it, the battle button asks for the selection instead of starting the battle.
        if (session.IsSpotLightNodePending(node.ChapterId, node.SectionId)) param.HPNNLMBHOPF = true;
        // Bonfire (幕后茶歇): param 11 marks the node, 15 lists its event, 3 shows the partner on it; the Hex display
        // (param 8 == 1) would win over the bonfire one.
        if (session.FindBonfireNode(node.ChapterId, node.SectionId) is { } bonfire)
        {
            param.FCNNBODJNIK = true;
            param.PFEMDGNFHNH = 0;
            param.HKKLNENEPNE.Add(bonfire.EventId);
            param.HOEKDPEPLIF.Add(new OEJKAOKHPPL { OKGGFNJJLNG = bonfire.PetPalEffectId });
        }
        return new BCCIJHPLJOJ
        {
            DCAHJELNJNP = node.ChapterId,
            SectionId = node.SectionId,
            Param = param,
        };
    }

    private GridFightLayerInfo BuildLayerInfo()
    {
        var session = Session!;
        var data = new GridFightLayerData
        {
            FightCampId = session.BattlePlan?.CampId ??
                          session.EncounterOptions.FirstOrDefault()?.BattlePlan.CampId ?? 0,
            SelectedEliteBranchId = session.CurrentNode.NodeType == Enums.GridFight.GridFightNodeTypeEnum.EliteBranch
                ? session.EliteBranchId
                : 1,
        };
        var plans = session.CurrentNode.NodeType == Enums.GridFight.GridFightNodeTypeEnum.EliteBranch &&
                    session.BattlePlan == null
            ? session.EncounterOptions.Select(option => (option.BattlePlan, Option: option)).ToList()
            : session.BattlePlan == null
                ? []
                : [(session.BattlePlan, (GridFightEncounterOptionState?)null)];
        foreach (var (plan, option) in plans)
        {
            var encounter = new GridFightEncounterInfo
            {
                EncounterId = option?.EncounterId ?? 0,
                EnemyDifficultyAdd = plan.DifficultyAdd,
                PenaltyRuleId = plan.PenaltyRuleId,
            };
            if (option != null)
                encounter.DropInfo = BuildEncounterDropInfo(option);
            foreach (var wavePlan in plan.Waves)
            {
                var wave = new GridFightMonsterWaveInfo { WaveId = wavePlan.WaveId };
                wave.MonsterList.Add(wavePlan.Monsters.Select(monster => new GridFightMonsterInfo
                {
                    MonsterId = monster.Monster.MonsterID,
                    RoleStar = monster.RoleStar,
                }));
                encounter.MonsterWaveList.Add(wave);
            }
            data.EncounterList.Add(encounter);
        }
        return new GridFightLayerInfo { Data = data };
    }

    private static GridFightDropInfo BuildEncounterDropInfo(GridFightEncounterOptionState option)
    {
        var result = new GridFightDropInfo();
        foreach (var drop in option.Rewards ?? [])
            result.DropList.Add(new GridFightDropEntry
            {
                DropType = drop.Type,
                ItemId = drop.ItemId,
                Num = drop.Num,
                Param = drop.Type == GridFightDropType.Role ? new GridFightDropParam { RoleStar = 1 } : null,
            });
        return result;
    }

    private GridFightGameTeamInfo BuildTeamInfo()
    {
        var team = new GridFightGameTeamInfo();
        team.GridGameRoleList.Add(Session!.Roles.Select(BuildRole));
        if (Session.PepeNpc != null)
            team.GridGameNpcList.Add(BuildPepeNpc(Session.PepeNpc));
        if (Session.AstaDogNpc != null)
            team.GridGameNpcList.Add(BuildPepeNpc(Session.AstaDogNpc));
        team.GridGameNpcList.Add(Session.LizhiNpcs.Select(BuildPepeNpc));
        team.GridGameNpcList.Add(Session.OwlbertNpcs.Select(BuildPepeNpc));
        team.ForgeItemList.Add(Session.ForgeItems.Select(BuildForgeItem));
        return team;
    }

    private GridGameRoleInfo BuildRole(GridFightRoleState role)
    {
        return BuildRole(role, role.RoleId);
    }

    private GridGameRoleInfo BuildRole(GridFightRoleState role, uint roleId)
    {
        var info = new GridGameRoleInfo
        {
            Id = roleId,
            UniqueId = role.UniqueId,
            RoleStar = role.Star,
            Pos = role.Position,
        };
        info.EquippedEquipmentUniqueIdList.Add(role.EquippedEquipmentUniqueIds);
        PopulateRolePropertyScales(info.ConvertPropertyToFixpoint, Session?.HasCyreneCombatEnhancement == true);
        Session?.ApplyInvestmentPropertyScales(info.ConvertPropertyToFixpoint);
        Session?.ApplyFateDivinerPropertyScales(info.ConvertPropertyToFixpoint, roleId);
        Session?.ApplySpotLightPropertyScales(info.ConvertPropertyToFixpoint, role.UniqueId);
        Session?.ApplyPermanentRolePropertyScales(info.ConvertPropertyToFixpoint, role);
        Session?.ApplyTrailblazeProperties(info.ConvertPropertyToFixpoint);
        if (Session != null)
            foreach (var (key, value) in Session.ResolveRoleSavedValues(role, roleId))
                info.GridFightValueInitComponent[key] = value;
        return info;
    }

    internal static void PopulateRolePropertyScales(
        IDictionary<uint, uint> properties,
        bool hasCyreneCombatEnhancement = false)
    {
        properties[32] = 180;
        properties[33] = 480;
        properties[34] = 440;
        properties[44] = 304;
        properties[49] = 180;
        properties[52] = 240;
        properties[53] = 480;
        properties[57] = 240;
        properties[59] = 450;
        properties[1013] = 100;
        properties[1014] = 100;
        properties[1019] = 100;
        properties[1025] = 100;
        if (!hasCyreneCombatEnhancement) return;
        properties[(uint)AvatarPropertyTypeEnum.ExtraAllDamageTypeAddedRatio1] = 1090;
        properties[(uint)AvatarPropertyTypeEnum.ExtraAllDamageReduce] = 990;
    }
}
