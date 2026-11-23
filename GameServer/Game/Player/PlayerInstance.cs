using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Mission;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Calyx;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Player;
using March7thHoney.Database.Scene;
using March7thHoney.Database.Tutorial;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Activity;
using March7thHoney.GameServer.Game.Activity.ExpeditionBattle;
using March7thHoney.GameServer.Game.Activity.FinalityBattle;
using March7thHoney.GameServer.Game.Activity.MechCraft;
using March7thHoney.GameServer.Game.Activity.B51Racing;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Calyx;
using March7thHoney.GameServer.Game.Challenge;
using March7thHoney.GameServer.Game.ChallengePeak;
using March7thHoney.GameServer.Game.Expedition;
using March7thHoney.GameServer.Game.Friend;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.Inventory;
using March7thHoney.GameServer.Game.Lineup;
using March7thHoney.GameServer.Game.Mail;
using March7thHoney.GameServer.Game.Message;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Pet;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.GameServer.Game.Quest;
using March7thHoney.GameServer.Game.Raid;
using March7thHoney.GameServer.Game.Rogue;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Game.Shop;
using March7thHoney.GameServer.Game.Sync.Player;
using March7thHoney.GameServer.Game.TrainCakeCatch;
using March7thHoney.GameServer.Game.TrainParty;
using March7thHoney.GameServer.Game.LevelTasks;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet.Send.DailyActive;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;
using OfferingManager = March7thHoney.GameServer.Game.Inventory.OfferingManager;

namespace March7thHoney.GameServer.Game.Player;

public partial class PlayerInstance(PlayerData data)
{
    #region Managers

    #region Basic Managers

    public AvatarManager? AvatarManager { get; private set; }
    public LineupManager? LineupManager { get; private set; }
    public InventoryManager? InventoryManager { get; private set; }
    public BattleManager? BattleManager { get; private set; }
    public SceneSkillManager? SceneSkillManager { get; private set; }
    public CalyxOverrideManager? CalyxOverrideManager { get; private set; }
    public BattleInstance? BattleInstance { get; set; }

    #endregion

    #region Shopping Managers

    public GachaManager? GachaManager { get; private set; }
    public ShopManager? ShopManager { get; private set; }
    public OfferingManager? OfferingManager { get; private set; }

    #endregion

    #region Quest & Mission Managers

    public MissionManager? MissionManager { get; private set; }
    public QuestManager? QuestManager { get; private set; }
    public RaidManager? RaidManager { get; private set; }
    public StoryLineManager? StoryLineManager { get; private set; }
    public MessageManager? MessageManager { get; private set; }
    public TaskManager? TaskManager { get; private set; }

    #endregion

    #region Activity Managers

    public ActivityManager? ActivityManager { get; private set; }
    public TrainCakeCatchManager? TrainCakeCatchManager { get; private set; }
    public FinalityBattleManager? FinalityBattleManager { get; private set; }
    public MechCraftManager? MechCraftManager { get; private set; }
    public B51RacingManager? B51RacingManager { get; private set; }
    public ExpeditionBattleManager? ExpeditionBattleManager { get; private set; }

    #endregion

    #region Others

    public MailManager? MailManager { get; private set; }
    public FriendManager? FriendManager { get; private set; }
    public ChallengeManager? ChallengeManager { get; private set; }
    public ChallengePeakManager? ChallengePeakManager { get; private set; }
    public ChallengeTierceManager? ChallengeTierceManager { get; private set; }
    public GridFightManager? GridFightManager { get; private set; }
    public RogueManager? RogueManager { get; private set; }
    public ExpeditionManager? ExpeditionManager { get; private set; }
    public TrainPartyManager? TrainPartyManager { get; private set; }

    #endregion

    #endregion

    #region Datas

    public PlayerData Data { get; set; } = data;
    public PlayerUnlockData? PlayerUnlockData { get; private set; }
    public FriendRecordData? FriendRecordData { get; private set; }
    public SceneData? SceneData { get; private set; }
    public HeartDialData? HeartDialData { get; private set; }
    public TutorialData? TutorialData { get; private set; }
    public TutorialGuideData? TutorialGuideData { get; private set; }
    public BattleCollegeData? BattleCollegeData { get; private set; }
    public CalyxOverrideData? CalyxOverrideData { get; private set; }
    public ServerPrefsData? ServerPrefsData { get; private set; }
    public SceneInstance? SceneInstance { get; private set; }
    public List<BasePlayerComponent> Components { get; } = [];
    public int Uid { get; set; }
    public Connection? Connection { get; set; }
    public bool Initialized { get; set; }
    public bool IsNewPlayer { get; set; }

    /// <summary>
    ///     Language-file code (CHS/CHT/EN/…) the client reported in PlayerLoginCsReq, used to localize friend-list
    ///     bot/command replies for this player. Session-only — re-read on every login — and defaults to the server
    ///     language until login sets it.
    /// </summary>
    public string Language { get; set; } = ConfigManager.Config.ServerOption.Language;

    /// <summary>
    ///     Language server-rendered text for this player is translated to: the persisted /language pin, falling back to
    ///     the client-reported <see cref="Language" />.
    /// </summary>
    public string EffectiveLanguage => Data.LanguagePin ?? Language;

    /// <summary>
    ///     Mission-system switch for this player: the persisted per-player override, falling back to the
    ///     server config. Reads before the managers are wired fall back to the config.
    /// </summary>
    public bool MissionEnabled =>
        MissionManager?.Data.MissionEnabled ??
        PendingMissionEnabledOverride ?? ConfigManager.Config.ServerOption.EnableMission;

    /// <summary>
    ///     Override to stamp onto the fresh MissionData during InitializeAsync. Set by PlayerResetHelper so
    ///     /mission start can turn the mission system on for a wiped account before it bootstraps.
    /// </summary>
    public bool? PendingMissionEnabledOverride { get; set; }

    public int NextBattleId { get; set; } = 0;
    public int ChargerNum { get; set; } = 0;
    public bool LoginAnnounceSent { get; set; }

    public uint ActiveFarmElementEntityId { get; set; }
    public Position? FarmElementReturnPos { get; set; }
    public Position? FarmElementReturnRot { get; set; }

    #endregion

    #region Initializers

    public PlayerInstance(int uid) : this(new PlayerData { Uid = uid })
    {
        // New player: only the cheap synchronous stat setup runs here. Manager wiring, DB
        // resolution and avatar/lineup seeding are deferred to the awaited InitializeAsync
        // (driven by OnGetToken) so construction never blocks on async work — no Task.Wait.
        IsNewPlayer = true;
        Data.WelcomeAnnouncePending = true;
        Data.NextStaminaRecover = Extensions.GetUnixSec() + GameConstants.STAMINA_RESERVE_RECOVERY_TIME;
        Data.Level = ConfigManager.Config.ServerOption.StartTrailblazerLevel;
        Data.Name = ConfigManager.Config.ServerOption.DefaultNickname;
        OnLevelChange();

        DatabaseHelper.SaveInstance(Data);
    }

    private async ValueTask InitialPlayerManager()
    {
        DatabaseHelper.CreateBatch(WireManagers);
        await NormalizePostLoad();
    }

    // Construct the managers and components and resolve the per-table DB data. Pure synchronous wiring: it runs inside
    // DatabaseHelper.CreateBatch, which inserts the rows a new player needs in one transaction.
    private void WireManagers()
    {
        Uid = Data.Uid;
        ActivityManager = new ActivityManager(this);
        AvatarManager = new AvatarManager(this)
        {
            Data =
            {
                DatabaseVersion = GameConstants.AvatarDbVersion
            }
        };

        LineupManager = new LineupManager(this);
        InventoryManager = new InventoryManager(this);
        BattleManager = new BattleManager(this);
        SceneSkillManager = new SceneSkillManager(this);
        CalyxOverrideManager = new CalyxOverrideManager(this);
        MissionManager = new MissionManager(this);
        GachaManager = new GachaManager(this);
        MessageManager = new MessageManager(this);
        MailManager = new MailManager(this);
        FriendManager = new FriendManager(this);
        ShopManager = new ShopManager(this);
        ChallengeManager = new ChallengeManager(this);
        ChallengePeakManager = new ChallengePeakManager(this);
        ChallengeTierceManager = new ChallengeTierceManager(this);
        GridFightManager = new GridFightManager(this);
        RogueManager = new RogueManager(this);
        RogueTournManager = new RogueTourn.RogueTournManager(this);
        RogueMagicManager = new RogueMagic.RogueMagicManager(this);
        ChessRogueManager = new ChessRogue.ChessRogueManager(this);
        ExpeditionManager = new ExpeditionManager(this);
        TrainPartyManager = new TrainPartyManager(this);
        TrainCakeCatchManager = new TrainCakeCatchManager(this);
        FinalityBattleManager = new FinalityBattleManager(this);
        MechCraftManager = new MechCraftManager(this);
        B51RacingManager = new B51RacingManager(this);
        ExpeditionBattleManager = new ExpeditionBattleManager(this);
        TaskManager = new TaskManager(this);
        RaidManager = new RaidManager(this);
        StoryLineManager = new StoryLineManager(this);
        QuestManager = new QuestManager(this);
        OfferingManager = new OfferingManager(this);

        PlayerUnlockData = InitializeDatabase<PlayerUnlockData>();
        SceneData = InitializeDatabase<SceneData>();
        HeartDialData = InitializeDatabase<HeartDialData>();
        TutorialData = InitializeDatabase<TutorialData>();
        TutorialGuideData = InitializeDatabase<TutorialGuideData>();
        ServerPrefsData = InitializeDatabase<ServerPrefsData>();
        BattleCollegeData = InitializeDatabase<BattleCollegeData>();
        CalyxOverrideData = InitializeDatabase<CalyxOverrideData>();
        CalyxOverrideManager!.AttachData(CalyxOverrideData);
        FriendRecordData = InitializeDatabase<FriendRecordData>();

        Components.Add(new SwitchHandComponent(this));
        Components.Add(new FiveDimComponent(this));
        Components.Add(new DailyActiveManager(this));
        Components.Add(new PetManager(this));
    }

    // Deterministic post-load fixups + condition-gated mission/quest acceptance. Runs after WireManagers.
    private async ValueTask NormalizePostLoad()
    {
        if ((int)(ServerPrefsData!.Version * 1000) != GameConstants.GameVersionInt)
        {
            ServerPrefsData.ServerPrefsDict.Clear();
            ServerPrefsData.Version = GameConstants.GameVersionInt / 1000d;
        }

        Data.LastActiveTime = Extensions.GetUnixSec();

        AvatarManager?.ClampSkillLevels();
        LineupManager!.RefreshTrialAvatarSlots();

        // Applied before the bootstrap below so a /mission start reset seeds the story roster and accepts
        // the opening mission even when the server-wide EnableMission is off.
        if (PendingMissionEnabledOverride.HasValue)
            MissionManager!.Data.EnableMissionOverride = PendingMissionEnabledOverride;

        AvatarManager!.NormalizeHeroCurPath();

        if (MissionEnabled)
        {
            await MissionManager!.RepairSkippedRogueTutorials();
            await MissionManager!.AcceptMainMissionByCondition();
            // Runs on every load, not just once: a save can stall at any point in the chain, and the
            // pass is a no-op when every accepted main already has a running sub-mission.
            await MissionManager.RepairStalledMainMissions(false);
        }

        FriendManager!.PruneExpiredDevelopmentRecords();

        await QuestManager!.AcceptQuestByCondition();
    }

    public T InitializeDatabase<T>() where T : BaseDatabaseDataHelper, new()
    {
        var instance = DatabaseHelper.Instance?.GetInstanceOrCreateNew<T>(Uid);
        return instance!;
    }

    /// <summary>
    ///     Idempotent player bootstrap: wires managers + resolves DB data, then (for a brand-new
    ///     account) seeds the starting avatars/lineup. Awaited on the GetToken path and by the
    ///     reset helper — replaces the constructor's Task.Run(...).Wait() sync-over-async.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        if (Initialized) return;
        await InitialPlayerManager();
        if (IsNewPlayer) await SeedNewPlayerAsync();
        Initialized = true;
    }

    private async ValueTask SeedNewPlayerAsync()
    {
        // 8002/8004/... are path variants of 8001 (the same base avatar on a different path). For a path,
        // AvatarManager.AddAvatar first looks up BaseAvatarID (= 8001) in FormalAvatars and then attaches the PathInfo,
        // so the base 8001 must be added before the 8002 path. Otherwise FormalAvatars holds no Trailblazer at all, and
        // a lineup referencing 8001 or 8002 finds nothing and ToProto throws on null.
        await AvatarManager!.AddAvatar(TrailblazerAvatar.MaleDestructionId);
        await AvatarManager.AddAvatar(TrailblazerAvatar.FemaleDestructionId);
        await AvatarManager.AddAvatar(AvatarManager.StarterCompanionAvatarId);
        if (MissionEnabled)
        {
            await LineupManager!.AddSpecialAvatarToCurTeam(10010050);
        }
        else
        {
            await LineupManager!.AddAvatarToCurTeam(TrailblazerAvatar.BaseAvatarId);
            Data.CurrentGender = Gender.Woman;
            Data.CurBasicType = TrailblazerAvatar.FemaleDestructionId;
            // FormalAvatar.AvatarId decides the path and gender the client shows (CurMultiPathAvatarType). AddAvatar
            // creates the avatar with AvatarId=8001 (male), so it must be switched to 8002 for the female Trailblazer
            // to show.
            var hero = AvatarManager!.GetHero();
            if (hero != null) hero.AvatarId = TrailblazerAvatar.FemaleDestructionId;
        }
    }

    #endregion

    #region Network

    public async ValueTask OnGetToken()
    {
        await InitializeAsync();
    }

    public async ValueTask OnLogin()
    {
        if (AvatarManager != null)
            await AvatarManager.GrantUnlockedStoryPaths(false);
        await HeadFrameService.RepairOwnedItem(this);

        CalyxOverrideManager?.ResetLineupOverride();
        await SendPacket(new PacketStaminaInfoScNotify(this));

        // Before the extra-lineup reset and the scene load: a trial left open at logout is abandoned.
        ActivityManager?.RescueTrialAtLogin();
        // Roll today's daily training window (and offer today's daily missions) before the client asks for it.
        // Silent: the client pulls quest and mission data after the login response.
        await GetComponent<DailyActiveManager>().OnLogin();

        // Before the runs are resurrected and the saved scene is loaded: the operator option drops both.
        if (ConfigManager.Config.ServerOption.ResetLocationOnLogin)
            await ResetLocationAtLogin();

        ChallengeManager?.ResurrectInstance();
        ChallengeTierceManager?.ResurrectInstance();

        var tierceLobbyEntry = ChallengeTierceManager?.ConsumeReloginRedirect();

        if (StoryLineManager != null)
            await StoryLineManager.OnLogin();

        if (RaidManager != null)
            await RaidManager.OnLogin();

        // An extra lineup outlives the logout only while the raid, story line or challenge that owns it is running.
        var extraLineupOwned = RaidManager!.Data.CurRaidId != 0 ||
                               StoryLineManager!.StoryLineData.CurStoryLineId != 0 ||
                               ChallengeManager!.ChallengeInstance != null;
        await LineupManager!.RepairAtLogin(extraLineupOwned);

        if (tierceLobbyEntry == null && RogueManager != null)
            await RogueManager.RestoreLineupAtLogin();

        if (tierceLobbyEntry is { } lobbyEntry)
            await EnterScene(lobbyEntry, 0, false);
        else
            await LoadScene(Data.PlaneId, Data.FloorId, Data.EntryId, Data.Pos!, Data.Rot!, false);
        if (SceneInstance == null) await EnterScene(DefaultSpawnEntryId, 0, false);

    }

    /// <summary>
    ///     <c>ServerOption.ResetLocationOnLogin</c>: abandon the raid and challenge runs behind the saved location and
    ///     move the saved location to the default spawn, so a login never resumes a run scene. A story line owns its own
    ///     saved position, so an active one is left untouched.
    /// </summary>
    private async ValueTask ResetLocationAtLogin()
    {
        if (StoryLineManager?.StoryLineData.CurStoryLineId > 0)
        {
            SceneLogger.Debug($"Player {Uid}: location reset on login skipped while story line " +
                             $"{StoryLineManager.StoryLineData.CurStoryLineId} is active.");
            return;
        }

        if (RaidManager != null) await RaidManager.AbandonAtLogin();
        ChallengeManager?.ClearInstance();
        ChallengeTierceManager?.ClearInstance();

        if (!TryPlaceAtEntrance(DefaultSpawnEntryId))
            SceneLogger.Debug($"Player {Uid}: location reset on login failed, default spawn {DefaultSpawnEntryId} is unusable.");
    }

    public async ValueTask TrySendWelcomeAnnounce()
    {
        if (LoginAnnounceSent || !Data.WelcomeAnnouncePending) return;

        var announceCfg = ConfigManager.Config.ServerOption.ServerAnnounce;
        if (announceCfg.EnableAnnounce && !string.IsNullOrWhiteSpace(announceCfg.AnnounceContent))
        {
            await SendPacket(new PacketServerAnnounceNotify(
                announceCfg.AnnounceContent,
                announceCfg.GetDurationSeconds(),
                announceCfg.GetBannerFrequencySeconds()));
            LoginAnnounceSent = true;
        }

        Data.WelcomeAnnouncePending = false;
        DatabaseHelper.MarkDirty(Uid);
    }

    /// <summary>Synchronous logout bookkeeping: stamps the active time and flushes manager state.</summary>
    public void OnLogout()
    {
        Data.LastActiveTime = Extensions.GetUnixSec();
        DatabaseHelper.MarkDirty(Uid);
        B51RacingManager?.ClearRace();
        GridFightManager?.Persist();
    }

    public async ValueTask SendPacket(BasePacket packet)
    {
        if (Connection?.IsOnline == true) await Connection.SendPacket(packet);
    }

    #endregion

    #region Actions

    public async ValueTask SetPlayerHeadFrameId(uint headFrameId, long expireTime)
    {
        Data.HeadFrame = new PlayerHeadFrameInfo
        {
            HeadFrameId = headFrameId,
            HeadFrameExpireTime = expireTime
        };

        await SendPacket(new PacketPlayerSyncScNotify([new PlayerBoardSync(this)]));
    }

    public async ValueTask SpendStamina(int staminaCost)
    {
        Data.Stamina -= staminaCost;
        await SendPacket(new PacketStaminaInfoScNotify(this));
        // Spent Trailblaze Power counts as using its virtual item ("Consume 120 Trailblaze Power").
        if (staminaCost > 0 && MissionManager != null)
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.UseItem,
                new ItemData { ItemId = (int)VirtualItemEnum.Stamina, Count = staminaCost });
    }

    public void OnAddExp()
    {
        GameData.PlayerLevelConfigData.TryGetValue(Data.Level, out var config);
        GameData.PlayerLevelConfigData.TryGetValue(Data.Level + 1, out var config2);
        if (config == null || config2 == null) return;
        var nextExp = config2.PlayerExp - config.PlayerExp;

        while (Data.Exp >= nextExp)
        {
            Data.Exp -= nextExp;
            Data.Level++;
            GameData.PlayerLevelConfigData.TryGetValue(Data.Level, out config);
            GameData.PlayerLevelConfigData.TryGetValue(Data.Level + 1, out config2);
            if (config == null || config2 == null) break;
            nextExp = config2.PlayerExp - config.PlayerExp;
        }

        OnLevelChange();
    }

    /// <summary>Re-evaluates level-gated main missions (TakeParam/BeginParam PlayerLevel) after a level-up.</summary>
    public async ValueTask OnLevelUp()
    {
        if (MissionEnabled) await MissionManager!.AcceptMainMissionByCondition();

        // Reaching DailyQuest.MinLevel fills today's daily training at once instead of on the next login.
        var daily = GetComponentOrDefault<DailyActiveManager>();
        if (daily != null && await daily.EnsureToday())
            await SendPacket(new PacketDailyActiveInfoNotify(daily));
    }

    public void OnLevelChange()
    {
        if (!ConfigManager.Config.ServerOption.AutoUpgradeWorldLevel) return;
        var worldLevel = 0;
        foreach (var level in GameConstants.UpgradeWorldLevel)
            if (level <= Data.Level)
                worldLevel++;

        if (Data.WorldLevel != worldLevel) Data.WorldLevel = worldLevel;
    }

    public async ValueTask OnStaminaRecover()
    {
        var sendPacket = false;
        while (Data.NextStaminaRecover <= Extensions.GetUnixSec())
        {
            if (Data.Stamina >= GameConstants.MAX_STAMINA)
            {
                if (Data.StaminaReserve >= GameConstants.MAX_STAMINA_RESERVE) // needn't recover
                    break;
                Data.StaminaReserve = Math.Min(Data.StaminaReserve + 1, GameConstants.MAX_STAMINA_RESERVE);
            }
            else
            {
                Data.Stamina++;
            }

            Data.NextStaminaRecover = Data.NextStaminaRecover + (Data.Stamina >= GameConstants.MAX_STAMINA
                ? GameConstants.STAMINA_RESERVE_RECOVERY_TIME
                : GameConstants.STAMINA_RECOVERY_TIME);
            sendPacket = true;
        }

        if (sendPacket) await SendPacket(new PacketStaminaInfoScNotify(this));
    }

    public async ValueTask OnHeartBeat()
    {
        await OnStaminaRecover();

        if (MissionManager != null)
            await MissionManager.HandleAllFinishType();

        if (SceneInstance != null)
            await SceneInstance.OnHeartBeat();

        if (OfferingManager != null)
            await OfferingManager.UpdateOfferingData();

        DatabaseHelper.MarkDirty(Uid);
    }

    public T GetComponent<T>() where T : BasePlayerComponent
    {
        return Components.OfType<T>().FirstOrDefault() ??
               throw new InvalidOperationException($"Component {typeof(T)} not found.");
    }

    /// <summary>Like <see cref="GetComponent{T}" />, but null before the components are registered.</summary>
    public T? GetComponentOrDefault<T>() where T : BasePlayerComponent
    {
        return Components.OfType<T>().FirstOrDefault();
    }

    #endregion

    #region Serialization

    public PlayerBasicInfo ToProto()
    {
        return Data.ToProto();
    }

    public PlayerSimpleInfo ToSimpleProto()
    {
        return Data.ToSimpleProto(FriendOnlineStatus.Online);
    }

    #endregion
}
