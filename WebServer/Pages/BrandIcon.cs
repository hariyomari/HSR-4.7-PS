using System.Net;

namespace March7thHoney.WebServer.Pages;

/// <summary>
///     Shared badge and favicon rendering for player pages and the admin console,
///     using <c>ServerOption.Auth.RegistrationPageIcon</c> or generated initials.
/// </summary>
public static class BrandIcon
{
    /// <summary>Badge contents: an &lt;img&gt; when the configured icon resolves to a picture, initials otherwise.</summary>
    public static string RenderMarkup(string? configuredIcon, string serverName)
    {
        var icon = (configuredIcon ?? string.Empty).Trim();

        if (icon.Length > 0 && TryCreateImageSource(icon, out var imageSource))
            return $"""<img src="{WebUtility.HtmlEncode(imageSource)}" alt="">""";

        var text = icon.Length > 0 ? icon : BuildInitials(serverName);
        return $"<span>{WebUtility.HtmlEncode(text)}</span>";
    }

    /// <summary>
    ///     Returns the configured image href or an inline SVG badge.
    /// </summary>
    public static string FaviconHref(string? configuredIcon, string serverName)
    {
        var icon = (configuredIcon ?? string.Empty).Trim();

        if (icon.Length > 0 && TryCreateImageSource(icon, out var imageSource))
            return imageSource;

        var text = icon.Length > 0 && icon.Length <= 4 ? icon : BuildInitials(serverName);
        var fontSize = text.Length >= 3 ? 22 : 30;
        var svg =
            $"""
             <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#5b7fe0"/><stop offset="1" stop-color="#a78bfa"/></linearGradient></defs><rect width="64" height="64" rx="16" fill="url(#g)"/><text x="32" y="32" fill="#ffffff" font-family="Segoe UI, system-ui, sans-serif" font-size="{fontSize}" font-weight="700" text-anchor="middle" dominant-baseline="central">{WebUtility.HtmlEncode(text)}</text></svg>
             """;

        return "data:image/svg+xml," + Uri.EscapeDataString(svg.Trim());
    }

    /// <summary>Up to two characters: one per word, or letter + first following digit/capital for one-word names.</summary>
    public static string BuildInitials(string? serverName)
    {
        var name = (serverName ?? string.Empty).Trim();
        if (name.Length == 0) return "M7";

        var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.FirstOrDefault(char.IsLetterOrDigit))
            .Where(char.IsLetterOrDigit)
            .ToArray();

        if (words.Length >= 2)
            return new string([words[0], words[1]]).ToUpperInvariant();

        // Prefer a digit or capital for single-word initials ("March7thHoney" -> "M7").
        var letters = name.Where(char.IsLetterOrDigit).ToArray();
        if (letters.Length == 0) return "M7";

        var second = letters.Skip(1).FirstOrDefault(c => char.IsDigit(c) || char.IsUpper(c));
        if (second == default) second = letters.Length > 1 ? letters[1] : letters[0];

        return new string([letters[0], second]).ToUpperInvariant();
    }

    private static bool TryCreateImageSource(string icon, out string source)
    {
        source = string.Empty;

        if (icon.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
            (Uri.TryCreate(icon, UriKind.Absolute, out var uri) &&
             (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            source = icon;
            return true;
        }

        if (!IsSupportedImagePath(icon))
            return false;

        if (TryCreateLocalImageDataUri(icon, out source))
            return true;

        source = icon.StartsWith('/') ? icon : "/" + icon.Replace('\\', '/').TrimStart('/');
        return true;
    }

    private static bool TryCreateLocalImageDataUri(string iconPath, out string source)
    {
        source = string.Empty;
        var normalizedPath = iconPath.Replace('\\', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            normalizedPath,
            Path.Combine(Environment.CurrentDirectory, normalizedPath)
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                var mimeType = GetImageMimeType(candidate);
                var bytes = File.ReadAllBytes(candidate);
                source = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                return true;
            }
            catch
            {
                // Fall back to rendering the configured value as a web path.
            }
        }

        return false;
    }

    private static bool IsSupportedImagePath(string value)
    {
        return GetImageMimeType(value) != "application/octet-stream";
    }

    private static string GetImageMimeType(string value)
    {
        return Path.GetExtension(value).ToLowerInvariant() switch
        {
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
