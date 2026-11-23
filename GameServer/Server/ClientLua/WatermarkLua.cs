using System.Globalization;
using System.Text;
using March7thHoney.Configuration;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.ClientLua;

/// <summary>
///     Builds the Lua that rewrites the client's VersionText overlay with the lines configured in
///     <c>ServerOption.Watermark</c>.
/// </summary>
public static class WatermarkLua
{
    /// <summary>The script for <paramref name="player" />; null when the watermark is off or both lines are blank.</summary>
    public static string? Build(PlayerInstance player)
    {
        var watermark = ConfigManager.Config.ServerOption.Watermark;
        if (!watermark.Enabled) return null;

        var nickname = player.Data.Name ?? "";
        var uid = player.Uid.ToString();
        var version = string.IsNullOrEmpty(ClientVersionCache.LatestVersion)
            ? $"WIN{GameConstants.GAME_VERSION}"
            : ClientVersionCache.LatestVersion;

        var line1 = BuildLine(watermark.Line1Template, nickname, uid, version, watermark);
        var line2 = BuildLine(watermark.Line2Template, nickname, uid, version, watermark);

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(line1)) lines.Add(line1);
        if (!string.IsNullOrWhiteSpace(line2)) lines.Add(line2);
        if (lines.Count == 0) return null;

        var luaText = EscapeLuaString(string.Join("\n", lines));
        var fontSize = watermark.FontSize.ToString(CultureInfo.InvariantCulture);

        var lua =
            "local function modify_texts()\n" +
            "    local vGo = CS.UnityEngine.GameObject.Find(\"VersionText\")\n" +
            "    if vGo == nil then return end\n" +
            "    local vt = vGo:GetComponent(\"Text\")\n" +
            "    vt.supportRichText = true\n" +
            "    vt.text = \"" + luaText + "\"\n" +
            "    vt.fontSize = " + fontSize + "\n" +
            "end\n" +
            "\n" +
            "local function on_error(err)\n" +
            "    local files = io.open(\"./error.txt\", \"w\")\n" +
            "    files:write(err)\n" +
            "    files:close()\n" +
            "end\n" +
            "\n" +
            "local status, err = pcall(modify_texts)\n" +
            "if not status then on_error(err) end\n";

        return lua;
    }

    private static string BuildLine(string template, string nickname, string uid, string version,
        WatermarkConfig watermark)
    {
        var line = template
            .Replace("{nickname}", nickname, StringComparison.Ordinal)
            .Replace("{uid}", uid, StringComparison.Ordinal)
            .Replace("{version}", version, StringComparison.Ordinal)
            .Replace("{serverName}", ConfigManager.Config.GameServer.GameServerName, StringComparison.Ordinal)
            .Replace("{serverId}", ConfigManager.Config.GameServer.GameServerId, StringComparison.Ordinal);

        if (!watermark.UseGradient) return line;
        return ApplyGradient(line, watermark.GradientStartColor, watermark.GradientEndColor);
    }

    private static string ApplyGradient(string line, string startColor, string endColor)
    {
        if (!TryParseHexColor(startColor, out var r0, out var g0, out var b0) ||
            !TryParseHexColor(endColor, out var r1, out var g1, out var b1))
        {
            return line;
        }

        var visible = 0;
        foreach (var c in line)
            if (c != ' ') visible++;

        var sb = new StringBuilder(line.Length * 24);
        var idx = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                sb.Append(' ');
                continue;
            }
            var t = visible <= 1 ? 0.0 : (double)idx / (visible - 1);
            var r = (int)(r0 + (r1 - r0) * t);
            var g = (int)(g0 + (g1 - g0) * t);
            var b = (int)(b0 + (b1 - b0) * t);
            sb.Append($"<color=#{r:X2}{g:X2}{b:X2}>").Append(c).Append("</color>");
            idx++;
        }
        return sb.ToString();
    }

    private static bool TryParseHexColor(string value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var hex = value.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex[1..];

        if (hex.Length != 6) return false;
        if (!int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g))
            return false;
        return int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    private static string EscapeLuaString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }
}
