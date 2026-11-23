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
using March7thHoney.GameServer.Game.Task;
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
    public ShopService? ShopService { get; private set; }
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
        ShopService = new ShopService(this);
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

        foreach (var avatar in AvatarManager?.Data.FormalAvatars ?? [])
        foreach (var path in avatar.PathInfos.Values)
        foreach (var skill in path.GetSkillTree())
        {
            GameData.AvatarSkillTreeConfigData.TryGetValue(skill.Key * 100 + 1, out var config);
            if (config == null) continue;
            path.GetSkillTree()[skill.Key] = Math.Min(skill.Value, config.MaxLevel); // limit skill level
        }

        foreach (var info in LineupManager!.GetAllLineup().SelectMany(lineupInfo => lineupInfo.BaseAvatars ?? []))
        {
            if (info.SpecialAvatarId > 0 &&
                GameData.SpecialAvatarData.TryGetValue(info.SpecialAvatarId, out var excel))
            {
                info.SpecialAvatarId = excel.SpecialAvatarID;
                AvatarManager!.GetTrialAvatar(excel.SpecialAvatarID)?.CheckLevel(Data.WorldLevel);
            }

            if (info.SpecialAvatarId > 0 &&
                GameData.SpecialAvatarData.TryGetValue(info.SpecialAvatarId * 10 + 0, out var e))
                AvatarManager!.GetTrialAvatar(e.SpecialAvatarID)?.CheckLevel(Data.WorldLevel);
        }

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

        foreach (var friendDevelopmentInfoPb in FriendRecordData!.DevelopmentInfos.ToArray())
            if (Extensions.GetUnixSec() - friendDevelopmentInfoPb.Time >=
                TimeSpan.TicksPerDay * 7 / TimeSpan.TicksPerSecond)
                FriendRecordData.DevelopmentInfos.Remove(friendDevelopmentInfoPb);

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
        // 8002/8004/... 是 8001 的命途变体（同一基础角色不同 path），AvatarManager.AddAvatar
        // 走 path 分支时会先在 FormalAvatars 里找 BaseAvatarID(=8001) 再挂 PathInfo，
        // 因此必须先添加基础 8001 再添加 8002 path，否则 FormalAvatars 里没有任何主角，
        // lineup 引用 8001/8002 都会 Find 不到导致 ToProto 抛 null
        await AddAvatar(8001);
        await AddAvatar(8002);
        await AddAvatar(1001);
        if (MissionEnabled)
        {
            await LineupManager!.AddSpecialAvatarToCurTeam(10010050);
        }
        else
        {
            await LineupManager!.AddAvatarToCurTeam(8001);
            Data.CurrentGender = Gender.Woman;
            Data.CurBasicType = 8002;
            // FormalAvatar.AvatarId 决定客户端显示的命途/性别（CurMultiPathAvatarType），
            // AddAvatar 创建时 AvatarId=8001（男），需要切到 8002 才会显示女主
            var hero = AvatarManager!.GetHero();
            if (hero != null) hero.AvatarId = 8002;
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

        if (LineupManager!.GetCurLineup() != null) // null -> ignore(new player)
        {
            if (LineupManager!.GetCurLineup()!.IsExtraLineup() &&
                RaidManager!.Data.CurRaidId == 0 && StoryLineManager!.StoryLineData.CurStoryLineId == 0 &&
                ChallengeManager!.ChallengeInstance == null)
            {
                LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, []);
                if (LineupManager!.GetCurLineup()!.IsExtraLineup()) await LineupManager!.SetCurLineup(0);
            }

            foreach (var lineup in LineupManager.Data.Lineups)
            {
                if (lineup.Value.BaseAvatars!.Count >= 5)
                    lineup.Value.BaseAvatars = lineup.Value.BaseAvatars.GetRange(0, 4);

                foreach (var avatar in lineup.Value.BaseAvatars!)
                    if (avatar.BaseAvatarId > 10000)
                    {
                        GameData.SpecialAvatarData.TryGetValue(avatar.BaseAvatarId, out var special);
                        if (special != null)
                        {
                            avatar.SpecialAvatarId = special.SpecialAvatarID;
                            avatar.BaseAvatarId = special.AvatarID;
                        }
                        else
                        {
                            GameData.SpecialAvatarData.TryGetValue(avatar.BaseAvatarId * 10 + Data.WorldLevel,
                                out special);
                            if (special != null)
                            {
                                avatar.SpecialAvatarId = special.SpecialAvatarID;
                                avatar.BaseAvatarId = special.AvatarID;
                            }
                        }
                    }
            }

            foreach (var avatar in LineupManager.GetCurLineup()!.BaseAvatars!)
            {
                var avatarData = AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                if (avatarData is { CurrentHp: <= 0 })
                    // revive
                    avatarData.CurrentHp = 2000;
            }
        }

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
            SceneLogger.Info($"Player {Uid}: location reset on login skipped while story line " +
                             $"{StoryLineManager.StoryLineData.CurStoryLineId} is active.");
            return;
        }

        if (RaidManager != null) await RaidManager.AbandonAtLogin();
        ChallengeManager?.ClearInstance();
        ChallengeTierceManager?.ClearInstance();

        if (!TryPlaceAtEntrance(DefaultSpawnEntryId))
            SceneLogger.Warn($"Player {Uid}: location reset on login failed, default spawn {DefaultSpawnEntryId} is unusable.");
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

    public void OnLogoutAsync()
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

    /// <summary>
    ///     Maps a Trailblazer path variant to the player's gender: variants pair as male (odd) / female (even = odd + 1).
    ///     Ids without a matching config entry, and players without a gender, are returned unchanged.
    /// </summary>
    public static int NormalizeHeroPathId(int pathId, Gender gender)
    {
        if (gender is not (Gender.Man or Gender.Woman)) return pathId;
        var maleId = pathId % 2 == 0 ? pathId - 1 : pathId;
        var id = gender == Gender.Woman ? maleId + 1 : maleId;
        return GameData.MultiplePathAvatarConfigData.TryGetValue(id, out var excel) && excel.BaseAvatarID == 8001
            ? id
            : pathId;
    }

    /// <summary>
    ///     Switches a multi-path character to <paramref name="type" /> (any variant of the path; Trailblazer ids are
    ///     normalized to the player's gender). Returns the avatar id actually stored, or 0 when the path does not
    ///     belong to that character.
    /// </summary>
    public async ValueTask<int> ChangeAvatarPathType(int baseAvatarId, MultiPathAvatarTypeEnum type)
    {
        if (!GameData.MultiplePathAvatarConfigData.TryGetValue((int)type, out var pathExcel)) return 0;
        var owner = AvatarManager!.GetFormalAvatar(baseAvatarId);
        if (owner == null || owner.BaseAvatarId != pathExcel.BaseAvatarID) return 0;
        baseAvatarId = owner.BaseAvatarId;

        FormalAvatarInfo avatar;
        int id;
        if (baseAvatarId == 8001)
        {
            id = NormalizeHeroPathId((int)type, Data.CurrentGender);
            avatar = owner;
            if (Data.CurBasicType == id && avatar.AvatarId == id) return id; // CurBasicType can desync from the hero record (e.g. stale seed data); check both
            Data.CurBasicType = id;
            // Set avatar path
            avatar.AvatarId = id;
            avatar.SetCurSp(0, LineupManager!.GetCurLineup()!.IsExtraLineup());
            // Save new skill tree
            avatar.CheckPathSkillTree();
        }
        else
        {
            id = (int)type;
            avatar = owner;
            avatar.AvatarId = id;
            avatar.SetCurSp(0, LineupManager!.GetCurLineup()!.IsExtraLineup());
            // Save new skill tree
            avatar.CheckPathSkillTree();
        }

        // The field actor is built from the current path. Rebuild it before the path notify,
        // or the world keeps the previous model.
        await RefreshAvatarActor(baseAvatarId);

        if (baseAvatarId == 8001)
        {
            await SendPacket(new PacketAvatarPathChangedNotify(8001, (MultiPathAvatarType)id));
            await SendPacket(new PacketPlayerSyncScNotify(AvatarManager!.GetHero()!));
        }
        else
        {
            await SendPacket(new PacketAvatarPathChangedNotify((uint)baseAvatarId, (MultiPathAvatarType)type));
            await SendPacket(new PacketPlayerSyncScNotify(avatar));
        }

        return id;
    }

    private async ValueTask RefreshAvatarActor(int baseAvatarId)
    {
        if (SceneInstance == null) return;
        var existing = SceneInstance.AvatarInfo.Values.FirstOrDefault(x => x.AvatarInfo.BaseAvatarId == baseAvatarId);
        if (existing == null) return;

        await existing.ClearAllBuff();
        SceneInstance.AvatarInfo.Remove(existing.EntityId);
        SceneInstance.Entities.Remove(existing.EntityId);
        await existing.OnDestroyInstance();

        var fresh = new AvatarSceneInfo(existing.AvatarInfo, existing.AvatarType, this)
        {
            EntityId = ++SceneInstance.LastEntityId
        };
        SceneInstance.AvatarInfo[fresh.EntityId] = fresh;
        SceneInstance.Entities[fresh.EntityId] = fresh;
        if (SceneInstance.LeaderEntityId == existing.EntityId)
            SceneInstance.LeaderEntityId = fresh.EntityId;

        await SendPacket(new PacketSceneGroupRefreshScNotify(this,
            new List<BaseGameEntity> { fresh }, new List<BaseGameEntity> { existing }));
    }

    public async ValueTask SetHeroGender(Gender gender)
    {
        if (gender is not (Gender.Man or Gender.Woman)) return;

        // Keep the current Path; hero ids pair as male(odd)/female(even) per Path.
        var pathBase = Data.CurBasicType;
        if (pathBase is < 8001 or > 8010) pathBase = 8001;
        if (pathBase % 2 == 0) pathBase -= 1;
        var newId = pathBase + (gender == Gender.Woman ? 1 : 0);
        if (!GameData.MultiplePathAvatarConfigData.ContainsKey(newId)) newId = gender == Gender.Woman ? 8002 : 8001;

        Data.CurrentGender = gender;
        Data.IsGenderSet = true;
        Data.CurBasicType = newId;
        await SendPacket(new PacketGetBasicInfoScRsp(this));

        var hero = AvatarManager!.GetHero();
        if (hero != null)
        {
            hero.AvatarId = newId;
            hero.CheckPathSkillTree();
            hero.SetCurSp(0, LineupManager!.GetCurLineup()?.IsExtraLineup() ?? false);

            await SendPacket(new PacketAvatarPathChangedNotify(8001, (MultiPathAvatarType)newId));
            await SendPacket(new PacketPlayerSyncScNotify(hero));
        }

        DatabaseHelper.MarkDirty(Uid);

        // Reload in place so the client rebuilds the Trailblazer with the new model.
        if (SceneInstance != null && Data.Pos != null && Data.Rot != null)
            await LoadScene(Data.PlaneId, Data.FloorId, Data.EntryId, Data.Pos, Data.Rot, true);
    }

    public ValueTask<Retcode> ChangeAvatarSkin(int avatarId, int skinId) =>
        AvatarManager!.ChangeSkin(avatarId, skinId);

    public async ValueTask<FormalAvatarInfo> MarkAvatar(int avatarId, bool isMarked, bool sendPacket = true)
    {
        var avatar = AvatarManager!.GetFormalAvatar(avatarId)!;
        avatar.IsMarked = isMarked;
        if (sendPacket) await SendPacket(new PacketPlayerSyncScNotify(avatar));
        return avatar;
    }

    public async ValueTask AddAvatar(int avatarId, bool sync = true, bool notify = true)
    {
        await AvatarManager!.AddAvatar(avatarId, sync, notify);
    }

    public async ValueTask SpendStamina(int staminaCost)
    {
        Data.Stamina -= staminaCost;
        await SendPacket(new PacketStaminaInfoScNotify(this));
        // Spent Trailblaze Power counts as using item 11 ("Consume 120 Trailblaze Power").
        if (staminaCost > 0 && MissionManager != null)
            await MissionManager.HandleFinishType(MissionFinishTypeEnum.UseItem,
                new ItemData { ItemId = 11, Count = staminaCost });
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
