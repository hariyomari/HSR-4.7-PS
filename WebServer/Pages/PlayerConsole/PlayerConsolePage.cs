using System.Net;
using System.Text.Json;
using March7thHoney.Util;

namespace March7thHoney.WebServer.Pages;

/// <summary>
///     Renders /player with inline styles, translations and scripts for the embedded game browser,
///     without static files or external dependencies.
/// </summary>
public static class PlayerConsolePage
{
    public static string Render(string serverName, string configuredIcon, string serverLanguage)
    {
        var displayName = LoginPlatformPage.DisplayName(serverName);
        var encodedDisplayName = WebUtility.HtmlEncode(displayName);
        var encodedTitle = WebUtility.HtmlEncode($"Player console - {displayName}");
        var iconMarkup = LoginPlatformPage.RenderIconMarkup(configuredIcon, displayName);
        var faviconHref = WebUtility.HtmlEncode(BrandIcon.FaviconHref(configuredIcon, displayName));
        var serverNameJson = JsonSerializer.Serialize(displayName, WebJsonContext.Default.String);
        var languageJson = JsonSerializer.Serialize(serverLanguage, WebJsonContext.Default.String);
        var versionJson = JsonSerializer.Serialize(GameConstants.GAME_VERSION, WebJsonContext.Default.String);

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <meta name="color-scheme" content="light">
  <link rel="icon" href="{{faviconHref}}">
  <title>{{encodedTitle}}</title>
  <style>
{{PlayerConsoleStyles.Render()}}
  </style>
</head>
<body>
  <section id="login-view" class="login-view">
    <div class="login-card">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true">{{iconMarkup}}</span>
        <div>
          <p class="eyebrow" data-i18n="login.eyebrow">Player console</p>
          <h1>{{encodedDisplayName}}</h1>
        </div>
      </div>
      <p class="muted small" data-i18n="login.intro"></p>
      <form id="login-form" class="stack" autocomplete="on">
        <label class="field">
          <span data-i18n="login.uid_label">UID</span>
          <input id="login-uid" name="uid" type="text" inputmode="numeric" autocomplete="username" required>
        </label>
        <label class="field">
          <span data-i18n="login.password_label">Password</span>
          <div class="input-row">
            <input id="login-password" name="password" type="password" autocomplete="current-password">
            <button id="login-toggle" class="btn ghost small" type="button">Show</button>
          </div>
        </label>
        <label class="remember">
          <input id="login-remember" type="checkbox">
          <span data-i18n="login.remember">Remember UID and password</span>
        </label>
        <button class="btn primary block" type="submit" data-i18n="login.submit">Sign in</button>
        <p id="login-message" class="message" role="status" aria-live="polite"></p>
      </form>
      <div class="login-links">
        <a href="/login-platform/register.html" data-i18n="login.register">Create account</a>
        <a href="/login-platform/forgot-password.html" data-i18n="login.forgot">Forgot password</a>
      </div>
      <div class="lang-switch" role="group">
        <button type="button" data-lang="zh-CN">中文</button>
        <button type="button" data-lang="en">English</button>
      </div>
    </div>
  </section>

  <div id="app" class="app hidden">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true">{{iconMarkup}}</span>
        <div>
          <strong>{{encodedDisplayName}}</strong>
          <span><span data-i18n="app.title">Player console</span> · v{{GameConstants.GAME_VERSION}}</span>
        </div>
      </div>
      <div class="topbar-actions">
        <span id="player-chip" class="chip subtle"></span>
        <span id="status-chip" class="chip"></span>
        <div class="lang-switch" role="group">
          <button type="button" data-lang="zh-CN">中文</button>
          <button type="button" data-lang="en">EN</button>
        </div>
        <button id="refresh-btn" class="btn ghost small" type="button" data-i18n="shell.refresh">Refresh</button>
        <button id="logout-btn" class="btn ghost small" type="button" data-i18n="shell.logout">Sign out</button>
      </div>
    </header>

    <nav id="tabs" class="tabs" aria-label="Console sections"></nav>

    <main class="content">
      <div id="view-root" class="view-root"></div>
    </main>
  </div>

  <div id="toast-host" class="toast-host" aria-live="polite"></div>
  <div id="modal-host"></div>

  <script>
{{PlayerConsoleI18n.Render()}}
    window.M7_CONSOLE_CONFIG = {
      serverName: {{serverNameJson}},
      language: {{languageJson}},
      version: {{versionJson}}
    };
  </script>
  <script>
{{PlayerConsoleScripts.Render()}}
  </script>
</body>
</html>
""";
    }
}
