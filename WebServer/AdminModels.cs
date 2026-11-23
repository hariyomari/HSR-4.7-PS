namespace March7thHoney.WebServer;

// Response payloads for the admin console beyond the account basics in WebModels.cs. Property names are the
// exact JSON keys (snake_case, unchanged by the camelCase policy) so the page can consume them directly.

// GET /admin/api/session
public sealed record AdminSessionData(
    long expires_at, string server_name, string version, string language, int active_sessions);

// GET /admin/api/sessions — every live connection, with or without a logged-in player
public sealed record SessionInfo(
    int uid, string nickname, int level, int world_level, string endpoint, string state, long conversation_id,
    int plane_id, int floor_id, int entry_id, bool in_battle, bool in_raid, string language, List<int> lineup,
    long last_active, bool has_player);

public sealed record SessionsData(List<SessionInfo> sessions, int count, int active);

public sealed record KickAllData(int kicked);

// GET /admin/api/players/{uid}
public sealed record AvatarBrief(int id, int level, int promotion, int rank);

public sealed record PlayerDetailData(
    int uid, bool exists, bool online, string name, string signature, int level, int exp, int world_level,
    int scoin, int hcoin, int mcoin, int stamina, double stamina_reserve, int head_icon, int plane_id, int floor_id,
    int entry_id, long last_active, long month_card_expire, int fake_time_date, int avatar_count,
    List<AvatarBrief> avatars, List<int> lineup, int finished_main_missions, int running_main_missions,
    int mail_count, int material_count, int equipment_count, int relic_count, bool in_battle, bool in_raid,
    string language);

// Mail
public sealed record MailAttachmentBrief(
    int item_id, int count, int level, int rank, int promotion, int talent_level);

public sealed record MailboxEntry(
    int mail_id, string sender, string title, string content, bool is_read, bool is_star, long send_time,
    long expire_time, int template_id, List<MailAttachmentBrief> attachments);

public sealed record MailboxData(int uid, List<MailboxEntry> mails, int count, bool online);

public sealed record MailSendData(int sent, int failed, List<string> errors, List<int> recipients);

// Announcements
public sealed record AnnounceSendData(int sent, long duration_seconds, long frequency_seconds);

public sealed record AnnounceConfigData(
    bool enabled, string content, string duration, string frequency, long duration_seconds,
    long frequency_seconds);

// Commands
public sealed record CommandCatalogItem(
    string name, string description, string usage, string permission, List<string> aliases,
    List<string> subcommands);

public sealed record CommandCatalogData(List<CommandCatalogItem> commands, int count);

// Settings
public sealed record AdminSettingField(
    string key, string group, string type, string value, bool restart, List<string>? options, bool configured);

public sealed record SettingsData(List<AdminSettingField> fields, bool public_mode);

public sealed record SettingsApplyData(
    int applied, List<string> errors, bool restart_required, bool persisted, List<AdminSettingField> fields);

// Hotfix
public sealed record HotfixEntry(
    string version, string asset_bundle_url, string ex_asset_bundle_url, string ex_resource_url, string lua_url,
    string ifix_url);

public sealed record HotfixData(
    string region, bool send_hotfix, bool fetch_remote, List<HotfixEntry> entries, List<string> regions);

// Logs
public sealed record LogEntry(long seq, long time, string module, string level, string message);

public sealed record LogsData(List<LogEntry> entries, long latest_seq, long oldest_seq, int buffered, bool truncated);

public sealed record LogFileInfo(string name, long size, long modified, bool active);

public sealed record LogFilesData(List<LogFileInfo> files, string directory);

// Maintenance
public sealed record MaintenanceData(
    int uid_count, int dirty_uids, long last_save, string db_type, string db_name, long db_size,
    int rate_limit_entries, int blocked_entries, long server_time, string version, string os, int processors,
    double process_memory_mb, double machine_total_mb, double machine_available_mb, long uptime_seconds,
    int threads, int connections, int admin_sessions, bool public_mode, List<string> handbook_languages,
    int gc_collections, double gc_heap_mb);

public sealed record SaveDatabaseData(bool saved, double seconds, int flushed);

public sealed record ReloadData(string target, string message);

public sealed record RateLimitEntry(
    string key, string action, int count, long window_start, long blocked_until, long updated_at);

public sealed record RateLimitData(List<RateLimitEntry> entries, int count, int blocked, long server_time);

public sealed record ClearedData(int cleared);

public sealed record AdminKeyRotateData(string admin_key, int revoked_sessions);

// GET /admin/api/gridfight/{uid} — live snapshot of an online player's Currency War run
public sealed record GridFightStatusData(int uid, bool online, string nickname, GridFightRunData? run);

public sealed record GridFightRunData(
    uint season, uint division, uint chapter, uint section, string phase, uint portal_id,
    uint gold, uint level, uint exp, uint max_level, uint hp, uint max_hp, uint free_refresh, uint win_streak,
    uint max_active, uint trailblaze_coin, uint trailblaze_spent, List<uint> investments,
    List<GridFightRoleBrief> roles, List<GridFightTraitBrief> traits, List<GridFightEquipmentBrief> equipments,
    List<GridFightConsumableBrief> consumables);

public sealed record GridFightRoleBrief(
    uint unique_id, uint role_id, uint avatar_id, uint cost, uint star, uint position, bool on_board, List<uint> equipment_ids);

public sealed record GridFightTraitBrief(uint trait_id, uint layer, int members);

public sealed record GridFightEquipmentBrief(uint unique_id, uint equipment_id, string category, bool equipped);

public sealed record GridFightConsumableBrief(uint item_id, uint count);

// GET /admin/api/gridfight/catalog — pickers for the Currency War panel
public sealed record GridFightCatalogData(
    uint season, List<GridFightCatalogRole> roles, List<GridFightCatalogAugment> augments, List<uint> traits,
    List<GridFightCatalogEquipment> equipments, List<uint> consumables, GridFightNameTable names);

public sealed record GridFightCatalogRole(uint id, uint avatar_id, uint cost, List<uint> traits);

public sealed record GridFightCatalogAugment(uint id, string quality, uint category);

public sealed record GridFightCatalogEquipment(uint id, string category);

public sealed record GridFightNameTable(
    Dictionary<string, string> augments, Dictionary<string, string> traits, Dictionary<string, string> portals,
    Dictionary<string, string> items, Dictionary<string, string> avatars);

// Warps — GET /admin/api/warps and /admin/api/warps/pool
public sealed record WarpBannerView(
    int id, string gacha_type, string version, int phase, List<int> rate_up5, List<int> rate_up4, long begin_time,
    long end_time, bool running, int default_pair_id);

public sealed record WarpManualEntryView(
    int gacha_id, long begin_time, long end_time, List<int>? rate_up5, List<int>? rate_up4, int paired_gacha_id);

public sealed record WarpRotationView(
    int days, string start, string start_phase, long period_begin, long period_end, string? current_phase,
    string? next_phase);

// story: ServerOption.Warps.Story as configured; story_by_chapter: ServerOption.Warps.StoryByChapter as configured (only
// matters while the story filter is in effect); story_active: it is in effect (not Manual mode). phase_lineup:
// Started | Overlap | Version (the effective value). collab: the collab banners; served with the current period as window
// while collab_banners is on, otherwise listed as history rows (never running).
public sealed record WarpsData(
    string mode, long now, WarpRotationView rotation, List<WarpBannerView> permanent, List<WarpBannerView> scheduled,
    List<WarpManualEntryView> manual, int pool_size, int phase_count, List<int> ineligible, bool story,
    bool story_by_chapter, bool story_active, string phase_lineup, bool collab_banners, List<WarpBannerView> collab);

// The rotation steps: every phase, or with the Version lineup the first phase of each version standing for the whole
// version. banner_ids: the lineup under the configured PhaseLineup setting.
public sealed record WarpPhaseView(string key, string version, int phase, long start_time, List<int> banner_ids);

// names: Config/Custom/WarpItemNames.json as loaded, language ("en", "zh-CN") -> item id -> name.
public sealed record WarpPoolData(
    List<WarpPhaseView> phases, List<WarpBannerView> banners, Dictionary<string, Dictionary<int, string>> names);

// Account clone & named save snapshots (Config/Snapshots)
public sealed record SnapshotView(
    string id, string name, string description, long created_at, int source_uid, string game_version,
    int player_level, int frontier_main_mission, int floor_id, int table_count);

public sealed record SnapshotsData(List<SnapshotView> snapshots);

public sealed record SnapshotWrap(SnapshotView snapshot);

public sealed record DeletedSnapshot(string id);

// A test account created from another save. generated_password: set only when the request left the password empty.
public sealed record TestAccountData(AccountSummary account, string? generated_password);
