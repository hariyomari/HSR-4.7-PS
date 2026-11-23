using System.Net;
using System.Text.Json;
using March7thHoney.Util;

namespace March7thHoney.WebServer.Pages;

/// <summary>
///     Shared layout, styles and Chinese/English helpers for player-facing /login-platform pages.
/// </summary>
public static class LoginPlatformPage
{
    public static string DisplayName(string serverName)
    {
        return string.IsNullOrWhiteSpace(serverName) ? "March7thHoney" : serverName.Trim();
    }

    /// <summary>Full HTML document. <paramref name="bodyHtml" /> goes inside the card; <paramref name="pageScript" /> runs after the helpers.</summary>
    public static string Wrap(string title, string serverName, string bodyHtml, string pageScript,
        string? pageStrings = null)
    {
        var displayName = DisplayName(serverName);
        var encodedTitle = WebUtility.HtmlEncode($"{title} - {displayName}");
        var serverNameJson = JsonSerializer.Serialize(displayName, WebJsonContext.Default.String);
        var faviconHref = WebUtility.HtmlEncode(
            BrandIcon.FaviconHref(ConfigManager.Config.ServerOption.Auth.RegistrationPageIcon, displayName));

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex">
  <meta name="color-scheme" content="light">
  <link rel="icon" href="{{faviconHref}}">
  <title>{{encodedTitle}}</title>
  <style>
{{Styles()}}
  </style>
</head>
<body>
  <main>
    <section class="card" aria-live="polite">
{{bodyHtml}}
    </section>
    <div class="lang-switch" role="group">
      <button type="button" data-lang="zh-CN">中文</button>
      <button type="button" data-lang="en">English</button>
    </div>
  </main>
  <script>
    window.LP_SERVER_NAME = {{serverNameJson}};
{{CommonScript()}}
{{pageStrings ?? ""}}
{{pageScript}}
  </script>
</body>
</html>
""";
    }

    public static string Brand(string iconMarkup, string headingKey, string headingFallback, string serverName)
    {
        return $$"""
      <div class="brand">
        <div class="mark" aria-hidden="true">{{iconMarkup}}</div>
        <div>
          <h1 data-i18n="{{WebUtility.HtmlEncode(headingKey)}}">{{WebUtility.HtmlEncode(headingFallback)}}</h1>
          <p class="subtitle">{{WebUtility.HtmlEncode(DisplayName(serverName))}}</p>
        </div>
      </div>
""";
    }

    public static string RenderIconMarkup(string configuredIcon, string serverName)
    {
        return BrandIcon.RenderMarkup(configuredIcon, DisplayName(serverName));
    }

    private static string Styles()
    {
        return """
    :root {
      color-scheme: light;
      --bg: #f4f7fb;
      --surface: #ffffff;
      --field: #f7f9fc;
      --line: #dce3ec;
      --line-2: #c7d2e0;
      --ink: #182230;
      --muted: #5f6f82;
      --soft: #8492a6;
      --accent: #477dd8;
      --accent-2: #2f68c5;
      --accent-soft: rgba(71, 125, 216, 0.13);
      --green: #16835b;
      --amber: #a96713;
      --red: #cc4545;
      --shadow: 0 24px 70px rgba(33, 52, 79, 0.14);
      --font: "Inter", "Segoe UI", system-ui, -apple-system, "PingFang SC", "Microsoft YaHei", "Noto Sans SC", sans-serif;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: var(--font);
      font-size: 14px;
      line-height: 1.5;
      color: var(--ink);
      background:
        radial-gradient(900px 500px at 15% -10%, rgba(71, 125, 216, 0.14), transparent 60%),
        radial-gradient(700px 400px at 110% 110%, rgba(115, 87, 200, 0.1), transparent 55%),
        var(--bg);
      -webkit-font-smoothing: antialiased;
    }
    main { min-height: 100vh; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; padding: 28px 20px; }
    .card { width: min(100%, 430px); padding: 32px; border: 1px solid var(--line); border-radius: 18px; background: var(--surface); box-shadow: var(--shadow); }
    .brand { display: flex; align-items: center; gap: 14px; margin-bottom: 24px; }
    .mark { display: grid; place-items: center; width: 46px; height: 46px; flex: 0 0 46px; border-radius: 12px; overflow: hidden; }
    .mark span { display: grid; place-items: center; width: 100%; height: 100%; border-radius: 12px; background: linear-gradient(135deg, var(--accent), #a78bfa); color: #fff; font-weight: 800; font-size: 16px; padding: 0 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mark img { width: 100%; height: 100%; object-fit: contain; display: block; }
    h1 { margin: 0; font-size: 22px; line-height: 1.2; letter-spacing: -0.01em; }
    .subtitle { margin: 3px 0 0; color: var(--muted); font-size: 13px; }
    form { display: grid; gap: 16px; }
    label { display: grid; gap: 6px; font-size: 12.5px; font-weight: 600; color: var(--muted); }
    .input-row { position: relative; display: flex; align-items: center; }
    input {
      width: 100%;
      min-height: 46px;
      padding: 11px 13px;
      border: 1px solid var(--line);
      border-radius: 10px;
      background: var(--field);
      color: var(--ink);
      font: inherit;
      font-size: 15px;
      outline: none;
      transition: border-color .15s ease, box-shadow .15s ease;
    }
    input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-soft); }
    input:disabled { color: var(--muted); cursor: not-allowed; opacity: .7; }
    input::placeholder { color: var(--soft); }
    .input-row input { padding-right: 64px; }
    .toggle { position: absolute; right: 8px; padding: 4px 8px; border: 0; border-radius: 6px; background: transparent; color: var(--muted); font: inherit; font-size: 12px; font-weight: 600; cursor: pointer; }
    .toggle:hover { color: var(--ink); background: var(--accent-soft); }
    button.primary {
      min-height: 46px;
      border: 0;
      border-radius: 10px;
      background: var(--accent);
      color: #fff;
      font: inherit;
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: background .15s ease, transform .05s ease;
    }
    button.primary:hover:not(:disabled) { background: var(--accent-2); }
    button.primary:active:not(:disabled) { transform: translateY(1px); }
    button.primary:disabled { cursor: not-allowed; opacity: .55; }
    .message { min-height: 20px; margin: 0; color: var(--muted); font-size: 13.5px; line-height: 1.45; }
    .message.success { color: var(--green); }
    .message.error { color: var(--red); }
    .hint { margin: 0; color: var(--soft); font-size: 12px; }
    .strength { display: grid; grid-template-columns: repeat(4, 1fr); gap: 4px; margin-top: 2px; }
    .strength span { height: 4px; border-radius: 4px; background: var(--line); transition: background .15s ease; }
    .strength[data-score="1"] span:nth-child(-n+1) { background: var(--red); }
    .strength[data-score="2"] span:nth-child(-n+2) { background: var(--amber); }
    .strength[data-score="3"] span:nth-child(-n+3) { background: var(--accent); }
    .strength[data-score="4"] span:nth-child(-n+4) { background: var(--green); }
    .result { display: none; margin-top: 20px; padding-top: 18px; border-top: 1px solid var(--line); color: var(--muted); font-size: 13.5px; line-height: 1.55; }
    .result.show { display: block; }
    .result strong { color: var(--ink); font-size: 16px; }
    .uid-box { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 8px 0 12px; padding: 12px 14px; border: 1px solid var(--line-2); border-radius: 10px; background: var(--field); }
    .uid-box code { font-size: 18px; font-weight: 700; color: var(--ink); }
    .links { display: flex; justify-content: center; gap: 16px; margin-top: 20px; font-size: 13px; flex-wrap: wrap; }
    .links a { color: var(--accent-2); text-decoration: none; }
    .links a:hover { text-decoration: underline; }
    .status { text-align: center; padding: 8px 0 4px; }
    .status .icon { display: grid; place-items: center; width: 56px; height: 56px; margin: 0 auto 16px; border-radius: 50%; font-size: 26px; font-weight: 800; }
    .status .icon.good { background: rgba(62, 207, 142, 0.16); color: var(--green); }
    .status .icon.bad { background: rgba(242, 109, 106, 0.16); color: var(--red); }
    .status h1 { margin-bottom: 8px; }
    .status p { color: var(--muted); }
    .lang-switch { display: flex; gap: 4px; }
    .lang-switch button { padding: 5px 12px; border: 0; border-radius: 999px; background: transparent; color: var(--soft); font: inherit; font-size: 12.5px; font-weight: 600; cursor: pointer; }
    .lang-switch button.active { background: var(--surface); color: var(--ink); }
    .lang-switch button:hover { color: var(--ink); }
    .hidden { display: none !important; }
    @media (max-width: 480px) {
      main { padding: 18px 14px; }
      .card { padding: 24px 20px; border-radius: 14px; }
    }
""";
    }

    private static string CommonScript()
    {
        return """
    window.LP = (() => {
      const STORAGE_KEY = "m7-lp-lang";
      const strings = {
        "zh-CN": {
          "lp.register": "创建账号",
          "lp.forgot": "忘记密码",
          "lp.reset": "重置密码",
          "lp.back_register": "注册账号",
          "lp.forgot_link": "忘记密码？",
          "lp.username": "用户名",
          "lp.email": "邮箱",
          "lp.password": "密码",
          "lp.confirm_password": "确认密码",
          "lp.new_password": "新密码",
          "lp.show": "显示",
          "lp.hide": "隐藏",
          "lp.request_failed": "请求失败，请稍后再试。",
          "lp.password_min": "密码至少需要 {min} 个字符。",
          "lp.password_mismatch": "两次输入的密码不一致。",
          "lp.strength.weak": "密码强度：弱",
          "lp.strength.fair": "密码强度：一般",
          "lp.strength.good": "密码强度：良好",
          "lp.strength.strong": "密码强度：强"
        },
        "en": {
          "lp.register": "Create account",
          "lp.forgot": "Forgot password",
          "lp.reset": "Reset password",
          "lp.back_register": "Create an account",
          "lp.forgot_link": "Forgot password?",
          "lp.username": "Username",
          "lp.email": "Email",
          "lp.password": "Password",
          "lp.confirm_password": "Confirm password",
          "lp.new_password": "New password",
          "lp.show": "Show",
          "lp.hide": "Hide",
          "lp.request_failed": "Request failed. Please try again later.",
          "lp.password_min": "Password must be at least {min} characters long.",
          "lp.password_mismatch": "Passwords do not match.",
          "lp.strength.weak": "Password strength: weak",
          "lp.strength.fair": "Password strength: fair",
          "lp.strength.good": "Password strength: good",
          "lp.strength.strong": "Password strength: strong"
        }
      };

      let lang = detectLanguage();

      function detectLanguage() {
        try {
          const stored = window.localStorage.getItem(STORAGE_KEY);
          if (stored && strings[stored]) return stored;
        } catch {
        }
        return String(navigator.language || "").toLowerCase().startsWith("zh") ? "zh-CN" : "en";
      }

      function extend(table) {
        for (const [code, entries] of Object.entries(table || {})) {
          strings[code] = { ...(strings[code] || {}), ...entries };
        }
      }

      function t(key, params) {
        let text = strings[lang]?.[key] ?? strings.en?.[key] ?? key;
        if (params) {
          for (const [name, value] of Object.entries(params)) text = text.split(`{${name}}`).join(String(value ?? ""));
        }
        return text;
      }

      function apply() {
        document.documentElement.lang = lang;
        for (const element of document.querySelectorAll("[data-i18n]")) element.textContent = t(element.dataset.i18n);
        for (const element of document.querySelectorAll("[data-i18n-placeholder]")) element.placeholder = t(element.dataset.i18nPlaceholder);
        for (const element of document.querySelectorAll("[data-lang]")) element.classList.toggle("active", element.dataset.lang === lang);
        const heading = document.querySelector("h1[data-i18n]");
        if (heading) document.title = `${t(heading.dataset.i18n)} - ${window.LP_SERVER_NAME || "March7thHoney"}`;
        document.dispatchEvent(new CustomEvent("lp:lang", { detail: lang }));
      }

      function setLanguage(next) {
        if (!strings[next]) return;
        lang = next;
        try { window.localStorage.setItem(STORAGE_KEY, next); } catch {
        }
        apply();
      }

      function setMessage(element, text, kind) {
        element.textContent = text;
        element.className = kind ? `message ${kind}` : "message";
      }

      function bindPasswordToggles() {
        for (const button of document.querySelectorAll("[data-toggle]")) {
          button.addEventListener("click", () => {
            const input = document.getElementById(button.dataset.toggle);
            if (!input) return;
            const reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            button.dataset.i18n = reveal ? "lp.hide" : "lp.show";
            button.textContent = t(button.dataset.i18n);
          });
        }
      }

      function scorePassword(value) {
        if (!value) return 0;
        let score = 0;
        if (value.length >= 8) score++;
        if (value.length >= 12) score++;
        if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
        if (/\d/.test(value) && /[^A-Za-z0-9]/.test(value)) score++;
        return Math.min(4, Math.max(1, score));
      }

      function bindStrength(inputId, meterId, labelId) {
        const input = document.getElementById(inputId);
        const meter = document.getElementById(meterId);
        const label = document.getElementById(labelId);
        if (!input || !meter) return;
        const update = () => {
          const score = scorePassword(input.value);
          meter.dataset.score = String(score);
          if (label) {
            const keys = ["", "lp.strength.weak", "lp.strength.fair", "lp.strength.good", "lp.strength.strong"];
            label.dataset.i18n = keys[score] || "";
            label.textContent = score ? t(keys[score]) : "";
          }
        };
        input.addEventListener("input", update);
        document.addEventListener("lp:lang", update);
      }

      async function post(path, payload) {
        const response = await fetch(path, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        const body = await response.json().catch(() => ({ retcode: -1, message: t("lp.request_failed") }));
        if (!response.ok || body.retcode !== 0) throw new Error(body.message || t("lp.request_failed"));
        return body;
      }

      function getOrCreateDeviceId() {
        const key = "march7thhoney.registration.device_id";
        try {
          const existing = window.localStorage.getItem(key);
          if (existing) return existing;
          const generated = window.crypto?.randomUUID
            ? window.crypto.randomUUID()
            : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
          window.localStorage.setItem(key, generated);
          return generated;
        } catch {
          return "";
        }
      }

      function getDeviceFingerprint() {
        const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || "";
        const size = window.screen ? `${window.screen.width}x${window.screen.height}x${window.screen.colorDepth}` : "";
        return JSON.stringify({
          userAgent: navigator.userAgent || "",
          language: navigator.language || "",
          platform: navigator.platform || "",
          timezone,
          size,
          cores: navigator.hardwareConcurrency || 0,
          touch: navigator.maxTouchPoints || 0
        });
      }

      function init() {
        for (const button of document.querySelectorAll("[data-lang]")) {
          button.addEventListener("click", () => setLanguage(button.dataset.lang));
        }
        bindPasswordToggles();
        apply();
      }

      return { t, extend, apply, init, setMessage, bindStrength, post, getOrCreateDeviceId, getDeviceFingerprint, get lang() { return lang; } };
    })();
""";
    }
}
