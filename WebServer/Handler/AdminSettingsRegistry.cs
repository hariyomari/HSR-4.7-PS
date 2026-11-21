using System.Globalization;
using March7thHoney.Configuration;
using March7thHoney.Enums;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.Util;

namespace March7thHoney.WebServer.Handler;

/// <summary>
///     Explicit, reflection-free map between the admin console's settings form and <see cref="ConfigContainer" />.
///     Only options that are safe to flip on a running server are listed; the ones read once at boot are flagged
///     with <c>restart</c> so the UI can say so. Secrets are write-only.
/// </summary>
public static class AdminSettingsRegistry
{
    public sealed record ApplyResult(int Applied, List<string> Errors, bool RestartRequired, bool Persisted);

    private sealed record SettingDef(
        string Key,
        string Group,
        string Type,
        Func<string> Get,
        Func<string, string?> Set,
        bool Restart,
        Func<List<string>>? Options,
        Func<bool>? Configured);

    private static ConfigContainer Config => ConfigManager.Config;

    public static List<AdminSettingField> Snapshot()
    {
        return BuildDefinitions()
            .Select(def => new AdminSettingField(
                def.Key,
                def.Group,
                def.Type,
                def.Type == "secret" ? "" : def.Get(),
                def.Restart,
                def.Options?.Invoke(),
                def.Configured?.Invoke() ?? true))
            .ToList();
    }

    public static ApplyResult Apply(Dictionary<string, string>? values)
    {
        var errors = new List<string>();
        var applied = 0;
        var restartRequired = false;
        if (values == null || values.Count == 0)
            return new ApplyResult(0, errors, false, false);

        var definitions = BuildDefinitions().ToDictionary(def => def.Key, StringComparer.Ordinal);
        foreach (var (key, rawValue) in values)
        {
            if (!definitions.TryGetValue(key, out var def))
            {
                errors.Add($"{key}: unknown setting");
                continue;
            }

            var value = rawValue ?? string.Empty;
            if (def.Type == "secret" && string.IsNullOrEmpty(value))
                continue; // blank secret means "keep the current value"

            var error = def.Set(value);
            if (error != null)
            {
                errors.Add($"{key}: {error}");
                continue;
            }

            applied++;
            if (def.Restart) restartRequired = true;
        }

        var persisted = false;
        if (applied > 0)
        {
            try
            {
                ConfigManager.SaveConfig();
                persisted = !ConfigManager.IsPublicMode;
            }
            catch (Exception ex)
            {
                errors.Add($"Config.json: {ex.Message}");
            }
        }

        return new ApplyResult(applied, errors, restartRequired, persisted);
    }

    private static IEnumerable<SettingDef> BuildDefinitions()
    {
        var option = Config.ServerOption;
        var auth = option.Auth;
        var anti = auth.AntiAbuse;
        var email = auth.Email;
        var smtp = email.Smtp;
        var announce = option.ServerAnnounce;
        var welcome = option.WelcomeMail;
        var watermark = option.Watermark;
        var log = option.LogOption;
        var time = option.ServerTime;
        var warps = option.Warps;
        var http = Config.HttpServer;
        var game = Config.GameServer;
        var serverConfig = option.ServerConfig;

        // --- server ---
        yield return Str("GameServer.GameServerName", "server", () => game.GameServerName, v => game.GameServerName = v, 1, 64);
        yield return Str("GameServer.GameServerDescription", "server", () => game.GameServerDescription, v => game.GameServerDescription = v, 0, 256);
        yield return Str("ServerOption.DefaultNickname", "server", () => option.DefaultNickname, v => option.DefaultNickname = v, 1, 32);
        yield return Int("ServerOption.StartTrailblazerLevel", "server", () => option.StartTrailblazerLevel, v => option.StartTrailblazerLevel = v, 1, 80);
        yield return Bool("ServerOption.AutoUpgradeWorldLevel", "server", () => option.AutoUpgradeWorldLevel, v => option.AutoUpgradeWorldLevel = v);
        yield return Bool("ServerOption.EnableMission", "server", () => option.EnableMission, v => option.EnableMission = v);
        yield return Bool("ServerOption.EnableQuest", "server", () => option.EnableQuest, v => option.EnableQuest = v);
        yield return Bool("ServerOption.AutoLightSection", "server", () => option.AutoLightSection, v => option.AutoLightSection = v);
        yield return Bool("ServerOption.ResetLocationOnLogin", "server", () => option.ResetLocationOnLogin, v => option.ResetLocationOnLogin = v);
        yield return Int("ServerOption.FarmingDropRate", "server", () => option.FarmingDropRate, v => option.FarmingDropRate = v, 1, 999);
        yield return Int("ServerOption.DailyResetHour", "server", () => option.DailyResetHour, v => option.DailyResetHour = v, 0, 23);
        yield return Bool("ServerOption.EnableMonthCard", "server", () => option.EnableMonthCard, v => option.EnableMonthCard = v);
        yield return Bool("ServerOption.EnableFakeRecharge", "server", () => option.EnableFakeRecharge, v => option.EnableFakeRecharge = v);
        yield return Bool("ServerOption.AutoCreateUser", "server", () => option.AutoCreateUser, v => option.AutoCreateUser = v);
        yield return Choice("ServerOption.Language", "server", () => option.Language, v => option.Language = v, LanguageOptions, restart: true);
        yield return Choice("ServerOption.FallbackLanguage", "server", () => option.FallbackLanguage, v => option.FallbackLanguage = v, LanguageOptions, restart: true);
        yield return Bool("ServerOption.UseCache", "server", () => option.UseCache, v => option.UseCache = v, restart: true);
        yield return Bool("ServerOption.LoadPausedModuleData", "server", () => option.LoadPausedModuleData, v => option.LoadPausedModuleData = v, restart: true);

        // --- network ---
        yield return Str("HttpServer.PublicAddress", "network", () => http.PublicAddress, v => http.PublicAddress = v, 1, 253);
        yield return Str("GameServer.PublicAddress", "network", () => game.PublicAddress, v => game.PublicAddress = v, 1, 253);
        yield return Int("GameServer.Port", "network", () => (int)game.Port, v => game.Port = (uint)v, 1, 65535, restart: true);
        yield return Bool("ServerOption.ServerConfig.RunDispatch", "network", () => serverConfig.RunDispatch, v => serverConfig.RunDispatch = v);
        yield return Str("ServerOption.ServerConfig.FromDispatchBaseUrl", "network", () => serverConfig.FromDispatchBaseUrl, v => serverConfig.FromDispatchBaseUrl = v, 0, 512);
        yield return Bool("HttpServer.SendHotfix", "network", () => http.SendHotfix, v => http.SendHotfix = v);
        yield return Bool("HttpServer.UseFetchRemoteHotfix", "network", () => http.UseFetchRemoteHotfix, v => http.UseFetchRemoteHotfix = v);
        yield return new SettingDef("HttpServer.TrustedProxies", "network", "string",
            () => string.Join(",", http.TrustedProxies ?? []), value =>
            {
                var addresses = value.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (addresses.Length > 64 || addresses.Any(ip => !System.Net.IPAddress.TryParse(ip, out _)))
                    return "expected up to 64 proxy IP addresses separated by commas";
                http.TrustedProxies = addresses.Distinct().ToList();
                return null;
            }, true, null, null);

        // --- auth ---
        yield return Bool("ServerOption.Auth.EnableRegistration", "auth", () => auth.EnableRegistration, v => auth.EnableRegistration = v);
        yield return Int("ServerOption.Auth.MinimumPasswordLength", "auth", () => auth.MinimumPasswordLength, v => auth.MinimumPasswordLength = v, 1, 128);
        yield return Int("ServerOption.Auth.PasswordHashIterations", "auth", () => auth.PasswordHashIterations, v => auth.PasswordHashIterations = v, 10000, 5000000);
        yield return Int("ServerOption.Auth.DispatchTokenExpireMinutes", "auth", () => auth.DispatchTokenExpireMinutes, v => auth.DispatchTokenExpireMinutes = v, 1, 5256000);
        yield return Int("ServerOption.Auth.ComboTokenExpireMinutes", "auth", () => auth.ComboTokenExpireMinutes, v => auth.ComboTokenExpireMinutes = v, 1, 5256000);
        yield return Str("ServerOption.Auth.RegistrationPageIcon", "auth", () => auth.RegistrationPageIcon, v => auth.RegistrationPageIcon = v, 0, 2048);
        yield return Bool("ServerOption.Auth.SendVerificationEmailOnRegister", "auth", () => auth.SendVerificationEmailOnRegister, v => auth.SendVerificationEmailOnRegister = v);
        yield return Bool("ServerOption.Auth.RequireVerifiedEmailForLogin", "auth", () => auth.RequireVerifiedEmailForLogin, v => auth.RequireVerifiedEmailForLogin = v);
        yield return Text("ServerOption.Auth.EmailVerificationRequiredMessage", "auth", () => auth.EmailVerificationRequiredMessage, v => auth.EmailVerificationRequiredMessage = v, 1, 512);
        yield return Int("ServerOption.Auth.EmailVerificationTokenExpireMinutes", "auth", () => auth.EmailVerificationTokenExpireMinutes, v => auth.EmailVerificationTokenExpireMinutes = v, 1, 525600);
        yield return Int("ServerOption.Auth.PasswordResetTokenExpireMinutes", "auth", () => auth.PasswordResetTokenExpireMinutes, v => auth.PasswordResetTokenExpireMinutes = v, 1, 525600);

        // --- anti-abuse ---
        yield return Bool("AntiAbuse.RateLimitEnabled", "antiabuse", () => anti.RateLimitEnabled, v => anti.RateLimitEnabled = v);
        yield return Int("AntiAbuse.RateLimitMaxRequests", "antiabuse", () => anti.RateLimitMaxRequests, v => anti.RateLimitMaxRequests = v, 1, 100000);
        yield return Int("AntiAbuse.RateLimitWindowSeconds", "antiabuse", () => anti.RateLimitWindowSeconds, v => anti.RateLimitWindowSeconds = v, 1, 86400);
        yield return Int("AntiAbuse.RateLimitBlockSeconds", "antiabuse", () => anti.RateLimitBlockSeconds, v => anti.RateLimitBlockSeconds = v, 1, 31536000);
        yield return Bool("AntiAbuse.LimitRegistrationPerIdentity", "antiabuse", () => anti.LimitRegistrationPerIdentity, v => anti.LimitRegistrationPerIdentity = v);
        yield return Choice("AntiAbuse.RegistrationIdentityMode", "antiabuse", () => anti.RegistrationIdentityMode, v => anti.RegistrationIdentityMode = v, () => ["DeviceOrIp", "DeviceOnly", "DeviceAndIp"]);
        yield return Bool("AntiAbuse.RequireRegistrationIdentity", "antiabuse", () => anti.RequireRegistrationIdentity, v => anti.RequireRegistrationIdentity = v);
        yield return Bool("AntiAbuse.LoginRateLimitEnabled", "antiabuse", () => anti.LoginRateLimitEnabled, v => anti.LoginRateLimitEnabled = v);
        yield return Int("AntiAbuse.LoginRateLimitMaxRequests", "antiabuse", () => anti.LoginRateLimitMaxRequests, v => anti.LoginRateLimitMaxRequests = v, 1, 100000);
        yield return Int("AntiAbuse.LoginRateLimitWindowSeconds", "antiabuse", () => anti.LoginRateLimitWindowSeconds, v => anti.LoginRateLimitWindowSeconds = v, 1, 86400);
        yield return Int("AntiAbuse.LoginRateLimitBlockSeconds", "antiabuse", () => anti.LoginRateLimitBlockSeconds, v => anti.LoginRateLimitBlockSeconds = v, 1, 31536000);
        yield return Bool("AntiAbuse.EmailRateLimitEnabled", "antiabuse", () => anti.EmailRateLimitEnabled, v => anti.EmailRateLimitEnabled = v);
        yield return Int("AntiAbuse.EmailRateLimitMaxRequests", "antiabuse", () => anti.EmailRateLimitMaxRequests, v => anti.EmailRateLimitMaxRequests = v, 1, 100000);
        yield return Int("AntiAbuse.EmailRateLimitWindowSeconds", "antiabuse", () => anti.EmailRateLimitWindowSeconds, v => anti.EmailRateLimitWindowSeconds = v, 1, 86400);
        yield return Int("AntiAbuse.EmailRateLimitBlockSeconds", "antiabuse", () => anti.EmailRateLimitBlockSeconds, v => anti.EmailRateLimitBlockSeconds = v, 1, 31536000);

        // --- email ---
        yield return Bool("Email.Enabled", "email", () => email.Enabled, v => email.Enabled = v);
        yield return Str("Email.PublicBaseUrl", "email", () => email.PublicBaseUrl, v => email.PublicBaseUrl = v, 0, 512);
        yield return Str("Email.LogoUrl", "email", () => email.LogoUrl, v => email.LogoUrl = v, 0, 2048);
        yield return Str("Email.Smtp.Host", "email", () => smtp.Host, v => smtp.Host = v, 0, 253);
        yield return Int("Email.Smtp.Port", "email", () => smtp.Port, v => smtp.Port = v, 1, 65535);
        yield return Bool("Email.Smtp.EnableSsl", "email", () => smtp.EnableSsl, v => smtp.EnableSsl = v);
        yield return Str("Email.Smtp.Username", "email", () => smtp.Username, v => smtp.Username = v, 0, 256);
        yield return Secret("Email.Smtp.Password", "email", () => !string.IsNullOrEmpty(smtp.Password), v => smtp.Password = v);
        yield return Str("Email.Smtp.FromAddress", "email", () => smtp.FromAddress, v => smtp.FromAddress = v, 0, 254);
        yield return Str("Email.Smtp.FromName", "email", () => smtp.FromName, v => smtp.FromName = v, 0, 128);
        yield return Str("Email.VerificationSubject", "email", () => email.VerificationSubject, v => email.VerificationSubject = v, 1, 256);
        yield return Text("Email.VerificationBody", "email", () => email.VerificationBody, v => email.VerificationBody = v, 1, 8192);
        yield return Str("Email.PasswordResetSubject", "email", () => email.PasswordResetSubject, v => email.PasswordResetSubject = v, 1, 256);
        yield return Text("Email.PasswordResetBody", "email", () => email.PasswordResetBody, v => email.PasswordResetBody = v, 1, 8192);

        // --- announce ---
        yield return Bool("ServerAnnounce.EnableAnnounce", "announce", () => announce.EnableAnnounce, v => announce.EnableAnnounce = v);
        yield return Text("ServerAnnounce.AnnounceContent", "announce", () => announce.AnnounceContent, v => announce.AnnounceContent = v, 0, 4096);
        yield return Duration("ServerAnnounce.Duration", "announce", () => announce.Duration, v => announce.Duration = v);
        yield return Duration("ServerAnnounce.BannerFrequency", "announce", () => announce.BannerFrequency, v => announce.BannerFrequency = v);

        // --- welcome mail ---
        yield return Bool("WelcomeMail.Enabled", "welcomemail", () => welcome.Enabled, v => welcome.Enabled = v);
        yield return Str("WelcomeMail.Sender", "welcomemail", () => welcome.Sender, v => welcome.Sender = v, 1, 64);
        yield return Str("WelcomeMail.Title", "welcomemail", () => welcome.Title, v => welcome.Title = v, 1, 128);
        yield return Text("WelcomeMail.Content", "welcomemail", () => welcome.Content, v => welcome.Content = v, 0, 4096);
        yield return Int("WelcomeMail.TemplateId", "welcomemail", () => welcome.TemplateId, v => welcome.TemplateId = v, 0, 100000);
        yield return Int("WelcomeMail.ExpireDays", "welcomemail", () => welcome.ExpireDays, v => welcome.ExpireDays = v, 1, 3650);
        yield return Bool("WelcomeMail.IsStar", "welcomemail", () => welcome.IsStar, v => welcome.IsStar = v);
        yield return new SettingDef("WelcomeMail.Items", "welcomemail", "items",
            () => FormatWelcomeItems(welcome.Items),
            value => ParseWelcomeItems(value, out var items) is { } error ? error : Assign(() => welcome.Items = items),
            false, null, null);

        // --- watermark ---
        yield return Bool("Watermark.Enabled", "watermark", () => watermark.Enabled, v => watermark.Enabled = v);
        yield return Str("Watermark.Line1Template", "watermark", () => watermark.Line1Template, v => watermark.Line1Template = v, 0, 256);
        yield return Str("Watermark.Line2Template", "watermark", () => watermark.Line2Template, v => watermark.Line2Template = v, 0, 256);
        yield return Double("Watermark.FontSize", "watermark", () => watermark.FontSize, v => watermark.FontSize = v, 8, 400);
        yield return Bool("Watermark.UseGradient", "watermark", () => watermark.UseGradient, v => watermark.UseGradient = v);
        yield return Color("Watermark.GradientStartColor", "watermark", () => watermark.GradientStartColor, v => watermark.GradientStartColor = v);
        yield return Color("Watermark.GradientEndColor", "watermark", () => watermark.GradientEndColor, v => watermark.GradientEndColor = v);

        // --- time ---
        yield return Bool("ServerTime.EnableFakeServerTime", "time", () => time.EnableFakeServerTime, v => time.EnableFakeServerTime = v);
        yield return new SettingDef("ServerTime.FixedDate", "time", "string",
            () => time.FixedDate,
            value =>
            {
                var trimmed = value.Trim();
                if (!string.Equals(trimmed, "today", StringComparison.OrdinalIgnoreCase) &&
                    !ServerTimeProvider.TryParseDateInput(trimmed, out _))
                    return "expected yyyy-MM-dd or today";
                time.FixedDate = trimmed;
                return null;
            }, false, null, null);

        // --- warps ---
        // Mode goes through WarpSchedule.SetMode so entering Manual / Rotation seeds the list and start date the
        // same way the warps panel does.
        yield return Choice("Warps.Mode", "warps", () => WarpSchedule.Mode.ToString(),
            v => WarpSchedule.SetMode(Enum.Parse<WarpScheduleModeEnum>(v), WarpSchedule.Now()),
            () => Enum.GetNames<WarpScheduleModeEnum>().ToList());
        yield return Int("Warps.RotationDays", "warps", () => warps.RotationDays, v => warps.RotationDays = v, 1, 365);
        yield return new SettingDef("Warps.RotationStart", "warps", "string",
            () => warps.RotationStart,
            value =>
            {
                var trimmed = value.Trim();
                if (trimmed.Length > 0 && !ServerTimeProvider.TryParseDateInput(trimmed, out _))
                    return "expected yyyy-MM-dd or empty";
                warps.RotationStart = trimmed;
                return null;
            }, false, null, null);
        yield return new SettingDef("Warps.RotationStartPhase", "warps", "string",
            () => warps.RotationStartPhase,
            value =>
            {
                var trimmed = value.Trim();
                if (trimmed.Length == 0) return Assign(() => warps.RotationStartPhase = "");
                if (WarpSchedule.Pool.FindPhase(trimmed) is not { } phase)
                    return "not a phase of the warp history pool";
                warps.RotationStartPhase = phase.Key;
                return null;
            }, false, null, null);

        // --- logging ---
        yield return Bool("LogOption.EnableGamePacketLog", "logging", () => log.EnableGamePacketLog, v => log.EnableGamePacketLog = v);
        yield return Bool("LogOption.LogPacketToConsole", "logging", () => log.LogPacketToConsole, v => log.LogPacketToConsole = v);
        yield return Bool("LogOption.DisableLogDetailPacket", "logging", () => log.DisableLogDetailPacket, v => log.DisableLogDetailPacket = v);
        yield return Bool("LogOption.SavePersonalDebugFile", "logging", () => log.SavePersonalDebugFile, v => log.SavePersonalDebugFile = v);
        yield return Bool("LogOption.LogResourceCacheLoading", "logging", () => log.LogResourceCacheLoading, v => log.LogResourceCacheLoading = v, restart: true);
    }

    #region Definition helpers

    private static SettingDef Bool(string key, string group, Func<bool> get, Action<bool> set, bool restart = false)
    {
        return new SettingDef(key, group, "bool", () => get() ? "true" : "false", value =>
        {
            if (!TryParseBool(value, out var parsed)) return "expected true or false";
            set(parsed);
            return null;
        }, restart, null, null);
    }

    private static SettingDef Int(string key, string group, Func<int> get, Action<int> set, int min, int max,
        bool restart = false)
    {
        return new SettingDef(key, group, "int", () => get().ToString(CultureInfo.InvariantCulture), value =>
        {
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return "expected an integer";
            if (parsed < min || parsed > max) return $"expected {min}..{max}";
            set(parsed);
            return null;
        }, restart, null, null);
    }

    private static SettingDef Double(string key, string group, Func<double> get, Action<double> set, double min,
        double max)
    {
        return new SettingDef(key, group, "double", () => get().ToString(CultureInfo.InvariantCulture), value =>
        {
            if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return "expected a number";
            if (parsed < min || parsed > max) return $"expected {min}..{max}";
            set(parsed);
            return null;
        }, false, null, null);
    }

    private static SettingDef Str(string key, string group, Func<string> get, Action<string> set, int minLength,
        int maxLength, bool restart = false)
    {
        return new SettingDef(key, group, "string", get, value =>
        {
            var trimmed = value.Trim();
            if (trimmed.Length < minLength) return minLength == 1 ? "cannot be empty" : $"at least {minLength} characters";
            if (trimmed.Length > maxLength) return $"at most {maxLength} characters";
            set(trimmed);
            return null;
        }, restart, null, null);
    }

    private static SettingDef Text(string key, string group, Func<string> get, Action<string> set, int minLength,
        int maxLength)
    {
        return new SettingDef(key, group, "text", get, value =>
        {
            var normalized = value.Replace("\r\n", "\n").Trim();
            if (normalized.Length < minLength) return minLength == 1 ? "cannot be empty" : $"at least {minLength} characters";
            if (normalized.Length > maxLength) return $"at most {maxLength} characters";
            set(normalized);
            return null;
        }, false, null, null);
    }

    private static SettingDef Choice(string key, string group, Func<string> get, Action<string> set,
        Func<List<string>> options, bool restart = false)
    {
        return new SettingDef(key, group, "choice", get, value =>
        {
            var trimmed = value.Trim();
            var match = options().FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
            if (match == null) return "not one of the allowed values";
            set(match);
            return null;
        }, restart, options, null);
    }

    private static SettingDef Secret(string key, string group, Func<bool> configured, Action<string> set)
    {
        return new SettingDef(key, group, "secret", () => "", value =>
        {
            set(value);
            return null;
        }, false, null, configured);
    }

    private static SettingDef Duration(string key, string group, Func<string> get, Action<string> set)
    {
        return new SettingDef(key, group, "duration", get, value =>
        {
            var trimmed = value.Trim();
            if (!DurationParser.TryParseSeconds(trimmed, out var seconds) || seconds <= 0)
                return "expected a duration such as 30m, 2h, 7d";
            set(trimmed);
            return null;
        }, false, null, null);
    }

    private static SettingDef Color(string key, string group, Func<string> get, Action<string> set)
    {
        return new SettingDef(key, group, "color", get, value =>
        {
            var trimmed = value.Trim();
            if (trimmed.Length is not (7 or 9) || trimmed[0] != '#' ||
                !trimmed[1..].All(Uri.IsHexDigit))
                return "expected #RRGGBB";
            set(trimmed.ToUpperInvariant());
            return null;
        }, false, null, null);
    }

    private static string? Assign(Action action)
    {
        action();
        return null;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                result = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "off":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static List<string> LanguageOptions()
    {
        var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "CHS", "CHT", "EN" };
        try
        {
            var directory = new DirectoryInfo(Config.Path.LanguagePath);
            if (directory.Exists)
                foreach (var file in directory.GetFiles("*.json"))
                    languages.Add(Path.GetFileNameWithoutExtension(file.Name));
        }
        catch
        {
            // Fall back to the built-in list.
        }

        return languages.ToList();
    }

    // One attachment per line: itemId:count[:rank[:level[:promotion[:talentLevel]]]]
    private static string FormatWelcomeItems(List<WelcomeMailItemConfig> items)
    {
        return string.Join("\n", items.Select(item =>
            string.Join(":", item.ItemId, item.Count, item.Rank, item.Level, item.Promotion, item.TalentLevel)));
    }

    private static string? ParseWelcomeItems(string value, out List<WelcomeMailItemConfig> items)
    {
        items = [];
        var lines = value.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split([':', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                return $"'{line}': expected itemId:count";

            var numbers = new int[6];
            numbers[3] = 1;
            for (var i = 0; i < parts.Length && i < numbers.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                    return $"'{line}': '{parts[i]}' is not a number";
            }

            if (numbers[0] <= 0 || numbers[1] <= 0)
                return $"'{line}': item id and count must be positive";

            items.Add(new WelcomeMailItemConfig
            {
                ItemId = numbers[0],
                Count = numbers[1],
                Rank = numbers[2],
                Level = numbers[3],
                Promotion = numbers[4],
                TalentLevel = numbers[5]
            });
        }

        return null;
    }

    #endregion
}
