using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Quests;
using March7thHoney.Proto;
using March7thHoney.Util;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;

namespace March7thHoney.Database.Player;

[DbTable("Player")]
public class PlayerData : BaseDatabaseDataHelper
{
    // New-player defaults: the female Destruction Trailblazer (8002), her head icon, and the starter phone,
    // chat, card, music and Pam cosmetics.
    public const int DefaultBasicType = 8002;
    public const int DefaultHeadIcon = 208002;
    public const int DefaultPhoneTheme = 221000;
    public const int DefaultChatBubble = 220000;
    public const int DefaultPersonalCard = 253000;
    public const int DefaultPhoneCase = 254000;
    public const int DefaultBgm = 210007;
    public const int DefaultPamSkin = 252000;

    public string? Name { get; set; } = "";
    public string? Signature { get; set; } = "";
    public int Birthday { get; set; } = 0;
    public int CurBasicType { get; set; } = DefaultBasicType;
    public int HeadIcon { get; set; } = DefaultHeadIcon;
    public int PhoneTheme { get; set; } = DefaultPhoneTheme;
    public int ChatBubble { get; set; } = DefaultChatBubble;
    public int PersonalCard { get; set; } = DefaultPersonalCard;
    public int PhoneCase { get; set; } = DefaultPhoneCase;
    public int CurrentBgm { get; set; } = DefaultBgm;
    public int CurrentPamSkin { get; set; } = DefaultPamSkin;
    // Recorded when a gender is chosen but not read for client visibility: older builds set it on every story-mode
    // login, so the pending gender prompt is derived from mission progress instead (AvatarManager.IsHeroGenderPending).
    public bool IsGenderSet { get; set; } = false;
    public Gender CurrentGender { get; set; } = Gender.Woman;
    public int Level { get; set; } = 1;
    public int Exp { get; set; } = 0;
    public int WorldLevel { get; set; } = 0;
    public int Scoin { get; set; } = 0; // Credits
    public int Hcoin { get; set; } = 0; // Jade
    public int Mcoin { get; set; } = 0; // Crystals
    public int TalentPoints { get; set; } = 0; // Rogue talent points
    public long MonthCardExpireTime { get; set; } = 0;
    public int LastMonthCardRewardDate { get; set; } = 0;
    public bool WelcomeAnnouncePending { get; set; } = false;

    public int Pet { get; set; } = 0;
    public int CurMusicLevel { get; set; }

    public int Stamina { get; set; } = GameConstants.MAX_STAMINA;
    public double StaminaReserve { get; set; } = 0;
    public long NextStaminaRecover { get; set; } = 0;

    public Position? Pos { get; set; }

    public Position? Rot { get; set; }

    public PlayerHeadFrameInfo HeadFrame { get; set; } = new();
    public List<int> PlayerOutfitList { get; set; } = [];

    public int PlaneId { get; set; }

    public int FloorId { get; set; }

    public int EntryId { get; set; }

    public long LastActiveTime { get; set; }

    public List<int> TakenLevelReward { get; set; } = [];
    public PrivacySettingsPb PrivacySettings { get; set; } = new();

    public int FakeTimeDate { get; set; } = 0; // yyyyMMdd, 0 = off

    // Language-file code pinned with /language; null follows the client-reported language.
    public string? LanguagePin { get; set; }

    public static PlayerData? GetPlayerByUid(long uid)
    {
        var result = DatabaseHelper.Instance?.GetInstance<PlayerData>((int)uid);
        return result;
    }

    public PlayerBasicInfo ToProto()
    {
        return new PlayerBasicInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Exp = (uint)Exp,
            WorldLevel = (uint)WorldLevel,
            Scoin = (uint)Scoin,
            Hcoin = (uint)Hcoin,
            Mcoin = (uint)Mcoin,
            Stamina = (uint)Stamina
        };
    }

    // 4.4 lobby member basic info (MOHAGOFLGAP = pre-4.2 LobbyPlayerBasicInfo)
    public MOHAGOFLGAP ToLobbyProto()
    {
        return new MOHAGOFLGAP
        {
            Nickname = Name,
            Level = (uint)Level,
            Icon = (uint)HeadIcon,
            Platform = PlatformType.Pc,
            Uid = (uint)Uid
        };
    }

    public PlayerSimpleInfo ToSimpleProto(FriendOnlineStatus status)
    {
        if (!GameData.ChatBubbleConfigData.ContainsKey(ChatBubble)) // to avoid npe
            ChatBubble = DefaultChatBubble;

        var info = new PlayerSimpleInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Signature = Signature,
            Uid = (uint)Uid,
            OnlineStatus = status,
            HeadIcon = (uint)HeadIcon,
            Platform = PlatformType.Pc,
            LastActiveTime = LastActiveTime,
            ChatBubbleId = (uint)ChatBubble,
            PersonalCard = (uint)PersonalCard,
            HeadFrameInfo = ToPublicHeadFrameProto(),
            Gender = (uint)CurrentGender
        };

        if (PlayerOutfitList.Count > 0)
            info.PlayerOutfitData = ToPlayerOutfitProto();

        var pos = 0;
        var instance = DatabaseHelper.Instance!.GetInstance<AvatarData>(Uid);
        if (instance == null)
        {
            // Handle the synthetic contacts (command bot / Global Chat): always online.
            var systemAssists = ConfigManager.Config.ServerOption.GetSystemProfileAssistInfo(Uid);
            if (systemAssists != null)
            {
                info.OnlineStatus = FriendOnlineStatus.Online;
                info.AssistSimpleInfoList.AddRange(
                    systemAssists.Select((x, index) =>
                        new AssistSimpleInfo
                        {
                            AvatarId = (uint)x.AvatarId,
                            Level = (uint)x.Level,
                            DressedSkinId = (uint)x.SkinId,
                            Pos = (uint)index
                        }));
            }

            return info;
        }

        foreach (var avatar in instance.AssistAvatars.Select(instance.FindByAnyId))
            if (avatar != null)
                info.AssistSimpleInfoList.Add(new AssistSimpleInfo
                {
                    AvatarId = (uint)avatar.AvatarId,
                    Level = (uint)avatar.Level,
                    DressedSkinId = (uint)avatar.GetCurPathInfo().Skin,
                    Pos = (uint)pos++
                });

        return info;
    }

    public PlayerDetailInfo ToDetailProto()
    {
        var info = new PlayerDetailInfo
        {
            Nickname = Name,
            Level = (uint)Level,
            Signature = Signature,
            IsBanned = false,
            HeadIcon = (uint)HeadIcon,
            Platform = PlatformType.Pc,
            Uid = (uint)Uid,
            WorldLevel = (uint)WorldLevel,
            RecordInfo = new PlayerRecordInfo(),
            HeadFrameInfo = ToPublicHeadFrameProto()
        };

        var avatarInfo = DatabaseHelper.Instance!.GetInstance<AvatarData>(Uid);
        var inventoryInfo = DatabaseHelper.Instance.GetInstance<InventoryData>(Uid);
        var questInfo = DatabaseHelper.Instance.GetInstance<QuestData>(Uid);

        if (avatarInfo == null || inventoryInfo == null || questInfo == null)
        {
            // Handle the synthetic contacts (command bot / Global Chat)
            var systemAssists = ConfigManager.Config.ServerOption.GetSystemProfileAssistInfo(Uid);
            if (systemAssists != null)
                info.AssistAvatarList.AddRange(
                    systemAssists.Select((x, index) =>
                        new DisplayAvatarDetailInfo
                        {
                            AvatarId = (uint)x.AvatarId,
                            Level = (uint)x.Level,
                            DressedSkinId = (uint)x.SkinId,
                            Pos = (uint)index
                        }));
            return info;
        }

        info.RecordInfo = new PlayerRecordInfo
        {
            DIOIIFAHAKK = (uint)avatarInfo.FormalAvatars.Count,
            NECKJBHEKHE = (uint)inventoryInfo.EquipmentItems.Select(x => x.ItemId).ToHashSet().Count,
            KCBINJNIBNO = (uint)inventoryInfo.RelicItems.Count,
            ALGEOLGNFBL = (uint)GameData.AchievementDataData.Values.Select(x => x.QuestID).ToHashSet()
                .Count(x => questInfo.Quests.GetValueOrDefault(x)?.QuestStatus is QuestStatus.QuestFinish
                    or QuestStatus.QuestClose), // count finished achievements
            MHOIJFEBAPA = (uint)GameData.BackGroundMusicData.Count
        };

        var pos = 0;
        foreach (var avatar in avatarInfo.AssistAvatars.Select(avatarInfo.FindByAnyId))
            if (avatar != null)
                info.AssistAvatarList.Add(avatar.ToDetailProto(pos++,
                    new PlayerDataCollection(this, inventoryInfo, new LineupInfo())));

        pos = 0;
        foreach (var avatar in avatarInfo.DisplayAvatars.Select(display =>
                     avatarInfo.FormalAvatars.Find(x => x.BaseAvatarId == display)))
            if (avatar != null)
                info.DisplayAvatarList.Add(avatar.ToDetailProto(pos++,
                    new PlayerDataCollection(this, inventoryInfo, new LineupInfo())));

        return info;
    }

    /// <summary>
    ///     The head frame other players see: an empty one when the player hides it. The client's
    ///     PlayerBoardInfo.CreateByPlayerDetailInfo decides whether to show a frame from HeadFrameInfo.IsValid alone
    ///     (item ID non-zero and not expired). The player's own profile card always receives the real head frame and
    ///     shows it according to the local setting.
    /// </summary>
    public HeadFrameInfo ToPublicHeadFrameProto() =>
        PrivacySettings.HideHeadFrame ? new HeadFrameInfo() : HeadFrame.ToProto();

    public CJLCPMDGIBO ToPlayerOutfitProto() =>
        new() { EAKOLIJOEPA = { PlayerOutfitList.Select(x => (uint)x) } };
}

[MemoryPackable]
public partial class PlayerHeadFrameInfo
{
    /// <summary>
    ///     Head frame expiry (timestamp in seconds) used in place of "valid for the current season" when the server has
    ///     no season end time: 2099-12-31 12:00:00 UTC. The client displays the value as a timestamp
    ///     ("限时头像框在#1前有效"), and xLua only accepts integers up to 2^53: long.MaxValue makes HeadIconModifyPanel
    ///     throw "beyond double int limits", and the whole page fails to switch.
    /// </summary>
    public const long NoSeasonExpireTime = 4102401600;

    public long HeadFrameExpireTime { get; set; }
    public uint HeadFrameId { get; set; }

    public HeadFrameInfo ToProto()
    {
        return new HeadFrameInfo
        {
            // Older saves stored long.MaxValue; clamp it into the range the client can represent before sending.
            HeadFrameExpireTime = Math.Min(HeadFrameExpireTime, NoSeasonExpireTime),
            HeadFrameItemId = HeadFrameId
        };
    }
}

[MemoryPackable]
public partial class PrivacySettingsPb
{
    public bool DisplayChallengeLineup { get; set; } = true;
    public bool DisplayActiveState { get; set; } = true;
    public bool DisplayRecentlyState { get; set; } = true;
    public bool DisplayBattleRecord { get; set; } = true;
    public bool DisplayCollection { get; set; } = true;

    // Hides the head frame (shown by default). The client's PlayerSettingData.IsDisplayHeadFrame is the negation of
    // this value: login reads it from PlayerSettingInfo.BOMGNMDOGLM, and a toggle sends the UpdatePlayerSetting oneof
    // BOMGNMDOGLM (fields 10 and 1514 in the 4.7 protocol). New members may only be appended at the end (MemoryPack).
    public bool HideHeadFrame { get; set; }

    public PlayerSettingInfo ToSettingProto()
    {
        return new PlayerSettingInfo
        {
            // TODO: DisplayActiveState, DisplayRecentlyState and DisplayBattleRecord are not sent. Their fields
            // have not been identified since 4.3 renumbered PlayerSettingInfo.
            GNFDDPEMMFN = DisplayChallengeLineup,
            FDHEPCKLCAM = DisplayCollection,
            BOMGNMDOGLM = HideHeadFrame
        };
    }
}
