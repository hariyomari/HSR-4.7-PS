namespace March7thHoney.WebServer;

// Named replacements for the former anonymous response payloads, so STJ can
// serialize them via source generation (AOT/trim safe). Property names are the
// exact JSON keys; the camelCase policy leaves these lower-first names unchanged.

// Generic envelope used by endpoints that only ever return null data.
public sealed record StatusResult(int retcode, string message, string? data = null);

// /player/* self-service endpoints (legacy /console/* aliases remain available to launchers)
public sealed record ConsoleResult(int retcode, string message, string output);

// /player — player self-service console (typed endpoints; ConsoleResult stays the launcher contract)
public sealed record ConsoleEnvelope<T>(int retcode, string message, T data);

public sealed record ConsolePlayerData(
    int uid, string username, string nickname, int level, int world_level, string role, bool online,
    bool has_password, List<string> permissions, long expires_at);

public sealed record ConsoleLoginData(string token, ConsolePlayerData player);

// Same shape as the admin CommandCatalogItem plus the per-player verdict, so the page can grey out
// commands the account's role does not grant instead of hiding why they fail.
public sealed record ConsoleCommandItem(
    string name, string description, string usage, string permission, List<string> aliases,
    List<string> subcommands, bool allowed);

public sealed record ConsoleCatalogData(
    List<ConsoleCommandItem> commands, int count, int allowed_count, string role, List<string> permissions);

// /handbook/languages
public sealed record HandbookLanguages(List<string> languages);

// ma-passport getConfig
public sealed record PassportConfigData(bool enable_email_captcha, bool disable_mmt, bool enable_ps_bind_account);
public sealed record PassportConfigResult(int retcode, string message, PassportConfigData data);

// shield loadConfig
public sealed record ThirdpartyLoginConfig(string token_type, int game_token_expires_in);

public sealed record ThirdpartyLoginConfigs(
    ThirdpartyLoginConfig tw, ThirdpartyLoginConfig ap, ThirdpartyLoginConfig fb, ThirdpartyLoginConfig gl);

public sealed record LoadConfigData(
    int id, string game_key, string client, string identity, bool guest, string ignore_versions,
    string scene, string name, bool disable_regist, bool enable_email_captcha, string[] thirdparty,
    bool disable_mmt, bool server_guest, Dictionary<string, string> thirdparty_ignore, bool enable_ps_bind_account,
    ThirdpartyLoginConfigs thirdparty_login_configs, bool initialize_firebase, bool bbs_auth_login,
    string[] bbs_auth_login_ignore, bool fetch_instance_id, bool enable_flash_login);

public sealed record LoadConfigResult(int retcode, string message, LoadConfigData data);

// passport appLoginByPassword / legacy login body
public sealed record PassportUserInfo(
    string aid, string mid, string account_name, string email, int is_email_verify, string area_code,
    string mobile, string safe_area_code, string safe_mobile, string realname, string identity_code,
    string rebind_area_code, string rebind_mobile, string rebind_mobile_time, string[] links, string country,
    string password_time, int is_adult, string unmasked_email, int unmasked_email_type);

public sealed record PassportToken(int token_type, string token);

public sealed record PassportExtUserInfo(string guardian_email, string birth);

public sealed record PassportLoginBodyData(
    string bind_email_action_ticket, string reactivate_action_token,
    PassportUserInfo user_info, PassportToken token, PassportExtUserInfo ext_user_info);

public sealed record PassportLoginBody(int retcode, string message, PassportLoginBodyData data);

// register
public sealed record RegAccount(string uid, string name, string email, string token, string is_email_verify);

public sealed record RegUserInfo(string aid, string mid, string account_name, string email, string is_email_verify);

public sealed record RegToken(int token_type, string token);

public sealed record RegData(RegAccount account, RegUserInfo user_info, RegToken token, bool email_sent);

public sealed record RegVerifyData(
    RegAccount account, RegUserInfo user_info, RegToken token, bool email_sent, bool email_verification_required);

public sealed record RegResult(int retcode, string message, RegData data);

public sealed record RegVerifyResult(int retcode, string message, RegVerifyData data);

// === admin panel ===
public sealed record AdminResult<T>(int retcode, string message, T data);

public sealed record EmptyData;

public sealed record AdminLoginData(string token, long expires_at);

public readonly record struct AccountSummary(
    int uid, string username, string email, bool email_verified, long email_verified_at, string nickname,
    int? level, bool has_password, string role, string permissions, string base_permissions,
    string effective_permissions, long dispatch_token_expires_at, long combo_token_expires_at, bool online,
    bool banned, string ban_reason, long ban_expires_at, long ban_created_at, bool ban_permanent,
    long ban_remaining_seconds, int identity_keys, int world_level,
    string registration_ip, string last_ip, string current_ip, long created_at, long last_seen_at,
    List<string> ip_addresses, List<string> device_ids, List<string> device_keys, bool delete_on_logout);

public sealed record AccountDeleteFailure(int uid, string message);
public sealed record BulkDeleteData(List<int> deleted_uids, List<int> missing_uids, List<AccountDeleteFailure> failed);
public sealed record IdentityBlockSummary(int id, string kind, string value, string identity_key, string reason,
    long created_at, long expires_at, bool active);
public sealed record IdentityBlocksData(List<IdentityBlockSummary> blocks);
public sealed record IdentityBlockWrap(IdentityBlockSummary block, int affected_accounts);

public sealed record AccountsData(List<AccountSummary> accounts, int count, List<string> roles, string default_role);

public sealed record OverviewStats(
    int total_accounts, int online_players, int banned_accounts, int unverified_email, int roles,
    int connections, int password_missing);

public sealed record OverviewServer(
    string name, string game_address, string database, long uptime_seconds, double process_memory_mb,
    long server_time, bool registration_enabled, bool limit_registration_per_identity, string version,
    double machine_total_mb, double machine_available_mb, long last_save, int dirty_uids, string os,
    bool announce_enabled, bool email_enabled, bool fake_time_enabled, string fake_date, bool public_mode);

public sealed record OnlinePlayer(int uid, string nickname, int level, string endpoint, string state);

public sealed record RoleCount(string role, int count);

public sealed record AttentionItem(int uid, string username, string email, string role, string reason);

public sealed record OverviewData(
    OverviewStats stats, OverviewServer server, List<OnlinePlayer> online_players,
    List<RoleCount> role_breakdown, List<AttentionItem> attention);

public sealed record RegistrationData(bool registration_enabled);

public sealed record IdentityLimitData(bool limit_registration_per_identity);

public sealed record AccessRole(string name, List<string> configured_permissions, List<string> effective_permissions);

public sealed record AccessData(
    List<AccessRole> roles, string default_role, List<string> default_permissions, List<string> known_permissions);

public sealed record ExecCommandData(string output, int target_uid);

public sealed record AccountWrap(AccountSummary account);

public sealed record AccountEmailWrap(AccountSummary account, bool email_sent);

public sealed record AccountOnlineWrap(AccountSummary account, bool was_online);

public sealed record DeletedUid(int uid);

// === website game-account provisioning (WebAccountRoutes) ===
public sealed record WebAccountResult<T>(int retcode, string message, T data);

public sealed record WebAccountError(int retcode, string message, string error);

public sealed record WebAccountInfo(
    int uid, string username, string email, string? nickname, int? level, bool banned, string ban_reason,
    bool online, long created_at, long last_seen_at);

public sealed record WebAccountWrap(WebAccountInfo account);

public sealed record WebAccountStatus(
    string server_name, string version, int accounts, int online, bool ready);
