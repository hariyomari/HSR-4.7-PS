namespace March7thHoney.WebServer.Pages;

public static class AdminPageStyles
{
    public static string Render()
    {
        return """
    :root {
      color-scheme: light;
      --bg: #f4f7fb;
      --bg-2: #eef3f8;
      --panel: #ffffff;
      --panel-2: #f7f9fc;
      --panel-3: #e8eef6;
      --line: #dce3ec;
      --line-2: #c7d2e0;
      --ink: #182230;
      --muted: #5f6f82;
      --soft: #8492a6;
      --accent: #477dd8;
      --accent-2: #2f68c5;
      --accent-soft: rgba(71, 125, 216, 0.13);
      --green: #16835b;
      --green-soft: rgba(22, 131, 91, 0.11);
      --amber: #a96713;
      --amber-soft: rgba(169, 103, 19, 0.12);
      --red: #cc4545;
      --red-soft: rgba(204, 69, 69, 0.11);
      --purple: #7357c8;
      --cyan: #137b92;
      --shadow: 0 16px 40px rgba(33, 52, 79, 0.12);
      --radius: 14px;
      --radius-sm: 9px;
      --topbar: 60px;
      --sidebar: 250px;
      --font: "Inter", "Segoe UI", system-ui, -apple-system, "PingFang SC", "Microsoft YaHei", "Noto Sans SC", sans-serif;
      --mono: "JetBrains Mono", "Cascadia Code", "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
    }

    * { box-sizing: border-box; }
    html { height: 100%; }
    body {
      margin: 0;
      min-height: 100vh;
      background: var(--bg);
      color: var(--ink);
      font-family: var(--font);
      font-size: 14px;
      line-height: 1.5;
      -webkit-font-smoothing: antialiased;
      -moz-osx-font-smoothing: grayscale;
    }
    a { color: var(--accent-2); }
    h1, h2, h3, h4 { margin: 0; font-weight: 700; letter-spacing: -0.01em; }
    h1 { font-size: 22px; }
    h2 { font-size: 20px; }
    h3 { font-size: 15px; }
    p { margin: 0; }
    code, pre, .mono { font-family: var(--mono); font-size: 12.5px; }
    pre { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
    .hidden { display: none !important; }
    .muted { color: var(--muted); }
    .small { font-size: 12px; }
    .bad-text { color: var(--red); }
    .pre-wrap { white-space: pre-wrap; }
    .eyebrow {
      margin: 0 0 4px;
      color: var(--accent-2);
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.12em;
      text-transform: uppercase;
    }
    ::selection { background: var(--accent-soft); }
    *:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }

    /* ---------------------------------------------------------- controls */
    button, input, select, textarea { font: inherit; color: inherit; }
    button { border: 0; background: none; cursor: pointer; }
    input, select, textarea {
      width: 100%;
      min-height: 38px;
      padding: 8px 12px;
      border: 1px solid var(--line);
      border-radius: var(--radius-sm);
      background: var(--bg-2);
      color: var(--ink);
      outline: none;
      transition: border-color .15s ease, box-shadow .15s ease;
    }
    input[type="checkbox"] { width: auto; min-height: 0; padding: 0; }
    input[type="color"] { width: 44px; padding: 3px; cursor: pointer; }
    input.compact { min-height: 32px; padding: 5px 9px; font-size: 12.5px; }
    textarea { min-height: 96px; resize: vertical; line-height: 1.45; }
    select { appearance: none; padding-right: 30px; background-image: linear-gradient(45deg, transparent 50%, var(--muted) 50%), linear-gradient(135deg, var(--muted) 50%, transparent 50%); background-position: calc(100% - 16px) 16px, calc(100% - 11px) 16px; background-size: 5px 5px; background-repeat: no-repeat; }
    select.inline-select { width: auto; min-width: 120px; }
    input:focus, select:focus, textarea:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-soft); }
    input:disabled, select:disabled, textarea:disabled { opacity: .55; cursor: not-allowed; }
    input::placeholder, textarea::placeholder { color: var(--soft); }
    datalist { display: none; }

    .btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      min-height: 38px;
      padding: 0 16px;
      border: 1px solid var(--line-2);
      border-radius: var(--radius-sm);
      background: var(--panel-2);
      color: var(--ink);
      font-weight: 600;
      white-space: nowrap;
      transition: background .15s ease, border-color .15s ease, transform .05s ease, opacity .15s ease;
    }
    .btn:hover:not(:disabled) { background: var(--panel-3); border-color: var(--line-2); }
    .btn:active:not(:disabled) { transform: translateY(1px); }
    .btn:disabled { opacity: .45; cursor: not-allowed; }
    .btn.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
    .btn.primary:hover:not(:disabled) { background: var(--accent-2); border-color: var(--accent-2); }
    .btn.danger { background: transparent; border-color: rgba(242, 109, 106, 0.5); color: var(--red); }
    .btn.danger:hover:not(:disabled) { background: var(--red-soft); }
    .btn.success { background: var(--green); border-color: var(--green); color: #fff; }
    .btn.ghost { background: transparent; border-color: var(--line); color: var(--muted); }
    .btn.ghost:hover:not(:disabled) { color: var(--ink); background: var(--panel-2); }
    .btn.small { min-height: 30px; padding: 0 11px; font-size: 12.5px; }
    .btn.block { width: 100%; }
    .btn.busy { position: relative; color: transparent !important; }
    .btn.busy::after {
      content: "";
      position: absolute;
      width: 14px; height: 14px;
      border: 2px solid rgba(255, 255, 255, 0.35);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin .7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .link-button { padding: 0; color: var(--accent-2); font-size: 12.5px; text-align: left; }
    .link-button:hover { text-decoration: underline; }
    .icon-btn { display: inline-grid; place-items: center; width: 38px; height: 38px; border-radius: var(--radius-sm); color: var(--muted); }
    .icon-btn:hover { background: var(--panel-2); color: var(--ink); }

    .segmented { display: inline-flex; padding: 3px; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--bg-2); gap: 2px; flex-wrap: wrap; }
    .segmented button { min-height: 30px; padding: 0 12px; border-radius: 7px; color: var(--muted); font-weight: 600; font-size: 12.5px; }
    .segmented button.active { background: var(--panel-3); color: var(--ink); }
    .segmented button:hover:not(.active) { color: var(--ink); }

    .switch { display: inline-flex; align-items: center; gap: 10px; cursor: pointer; user-select: none; color: var(--ink); font-weight: 500; font-size: 13px; }
    .switch input { position: absolute; opacity: 0; width: 0; height: 0; }
    .switch-track { position: relative; width: 40px; height: 22px; flex: 0 0 40px; border-radius: 22px; background: var(--line-2); transition: background .15s ease; }
    .switch-track::after { content: ""; position: absolute; top: 3px; left: 3px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: transform .15s ease; }
    .switch input:checked + .switch-track { background: var(--green); }
    .switch input:checked + .switch-track::after { transform: translateX(18px); }
    .switch input:focus-visible + .switch-track { box-shadow: 0 0 0 3px var(--accent-soft); }
    .switch input:disabled + .switch-track { opacity: .5; }
    .switch-label { color: var(--muted); }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      min-height: 22px;
      padding: 1px 9px;
      border: 1px solid var(--line-2);
      border-radius: 999px;
      background: var(--panel-2);
      color: var(--ink);
      font-size: 11.5px;
      font-weight: 600;
      line-height: 1.3;
      white-space: nowrap;
      text-decoration: none;
    }
    .chip.good { background: var(--green-soft); border-color: rgba(62, 207, 142, 0.35); color: var(--green); }
    .chip.warn { background: var(--amber-soft); border-color: rgba(240, 180, 90, 0.35); color: var(--amber); }
    .chip.bad { background: var(--red-soft); border-color: rgba(242, 109, 106, 0.35); color: var(--red); }
    .chip.accent { background: var(--accent-soft); border-color: rgba(91, 141, 239, 0.4); color: var(--accent-2); }
    .chip.subtle { background: transparent; color: var(--muted); }
    .chip.clickable { cursor: pointer; }
    .chip.clickable:hover { border-color: var(--accent); color: var(--accent-2); }
    .chip.removable { padding-right: 4px; }
    .chip-remove { display: inline-grid; place-items: center; width: 18px; height: 18px; border-radius: 50%; color: var(--muted); font-size: 14px; line-height: 1; }
    .chip-remove:hover { background: var(--red-soft); color: var(--red); }
    .chip-list { display: inline-flex; flex-wrap: wrap; gap: 6px; align-items: center; }

    /* ------------------------------------------------------------- login */
    .login-view { min-height: 100vh; display: grid; place-items: center; padding: 24px; background: radial-gradient(1200px 600px at 20% -10%, rgba(71, 125, 216, 0.14), transparent 60%), radial-gradient(900px 500px at 110% 110%, rgba(115, 87, 200, 0.1), transparent 55%), var(--bg); }
    .login-card { width: min(100%, 420px); padding: 32px; border: 1px solid var(--line); border-radius: 18px; background: var(--panel); box-shadow: var(--shadow); }
    .login-brand { display: flex; align-items: center; gap: 14px; margin-bottom: 24px; }
    .brand-mark { display: grid; place-items: center; width: 46px; height: 46px; flex: 0 0 46px; border-radius: 12px; overflow: hidden; background: linear-gradient(135deg, var(--accent), var(--purple)); color: #fff; font-weight: 800; font-size: 16px; letter-spacing: 0.02em; }
    .brand-mark img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .brand-mark span { display: grid; place-items: center; width: 100%; height: 100%; padding: 0 3px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .login-card .message { min-height: 20px; color: var(--muted); font-size: 13px; }
    .login-card .message.bad { color: var(--red); }
    .login-card .message.good { color: var(--green); }
    .lang-switch { display: flex; justify-content: center; gap: 4px; margin-top: 18px; }
    .lang-switch button { padding: 4px 10px; border-radius: 999px; color: var(--muted); font-size: 12.5px; font-weight: 600; }
    .lang-switch button.active { background: var(--panel-3); color: var(--ink); }
    .lang-switch button:hover { color: var(--ink); }

    /* ------------------------------------------------------------- shell */
    .app { display: flex; flex-direction: column; min-height: 100vh; }
    .topbar {
      position: sticky; top: 0; z-index: 30;
      display: flex; align-items: center; gap: 14px;
      height: var(--topbar); padding: 0 18px;
      background: rgba(255, 255, 255, 0.88); backdrop-filter: blur(10px);
      border-bottom: 1px solid var(--line);
    }
    .topbar .brand { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .topbar .brand .brand-mark { width: 34px; height: 34px; flex-basis: 34px; border-radius: 9px; font-size: 13px; }
    .topbar .brand strong { display: block; font-size: 14px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .topbar .brand span { display: block; color: var(--muted); font-size: 11.5px; }
    .topbar-actions { display: flex; align-items: center; gap: 8px; margin-left: auto; }
    .topbar .lang-switch { margin: 0; }
    .layout { display: flex; flex: 1; min-height: 0; }
    .sidebar {
      position: sticky; top: var(--topbar);
      width: var(--sidebar); flex: 0 0 var(--sidebar);
      height: calc(100vh - var(--topbar));
      display: flex; flex-direction: column;
      padding: 16px 12px;
      border-right: 1px solid var(--line);
      background: var(--bg-2);
      overflow-y: auto;
    }
    .nav-group { margin-bottom: 14px; }
    .nav-group-title { margin: 0 0 6px 12px; color: var(--soft); font-size: 11px; font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase; }
    .nav-item { display: flex; align-items: center; gap: 12px; padding: 9px 12px; border-radius: 10px; color: var(--muted); text-decoration: none; transition: background .12s ease, color .12s ease; }
    .nav-item:hover { background: var(--panel); color: var(--ink); }
    .nav-item.active { background: var(--accent-soft); color: var(--ink); }
    .nav-item.active .nav-icon { color: var(--accent-2); }
    .nav-icon { display: grid; place-items: center; width: 22px; height: 22px; flex: 0 0 22px; }
    .nav-icon svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
    .nav-text { display: flex; flex-direction: column; min-width: 0; }
    .nav-text strong { font-size: 13.5px; font-weight: 600; }
    .nav-text span { font-size: 11.5px; color: var(--soft); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sidebar-foot { margin-top: auto; padding: 12px; color: var(--soft); font-size: 11.5px; line-height: 1.6; border-top: 1px solid var(--line); }
    .sidebar-foot strong { color: var(--muted); font-weight: 600; }
    .content { flex: 1; min-width: 0; padding: 22px 26px 60px; }
    .module-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 18px; flex-wrap: wrap; }
    .module-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .module-root { display: flex; flex-direction: column; gap: 18px; }

    /* ------------------------------------------------------------ panels */
    .panel { border: 1px solid var(--line); border-radius: var(--radius); background: var(--panel); min-width: 0; }
    .panel-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 18px; border-bottom: 1px solid var(--line); flex-wrap: wrap; }
    .panel-body { padding: 16px 18px; }
    .panel-body + .panel-body { border-top: 1px solid var(--line); }
    .grid { display: grid; gap: 18px; }
    .grid.two { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .split { display: grid; grid-template-columns: minmax(0, 1.35fr) minmax(0, 1fr); gap: 18px; align-items: start; }
    .console-split { grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr); }
    .stack { display: flex; flex-direction: column; gap: 14px; }
    .row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .space-between { justify-content: space-between; }
    .section-title { margin-top: 6px; color: var(--muted); font-size: 12px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
    .empty { padding: 34px 18px; color: var(--muted); text-align: center; }
    .empty.bad { color: var(--red); }
    .empty.loading { position: relative; }
    .notice { padding: 12px 14px; border: 1px solid var(--line-2); border-radius: var(--radius-sm); background: var(--panel-2); font-size: 13px; }
    .notice.good { border-color: rgba(62, 207, 142, 0.35); background: var(--green-soft); }
    .notice.warn { border-color: rgba(240, 180, 90, 0.35); background: var(--amber-soft); }
    .notice.bad { border-color: rgba(242, 109, 106, 0.35); background: var(--red-soft); }
    .error-list { margin: 8px 0 0; padding-left: 18px; color: var(--muted); }
    .key-notice { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .key-value { display: block; margin-top: 8px; padding: 8px 10px; border-radius: 7px; background: var(--bg); font-size: 14px; user-select: all; }

    .metric-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
    .metric-grid.compact { grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); }
    .metric { display: flex; flex-direction: column; gap: 4px; padding: 14px 16px; border: 1px solid var(--line); border-radius: var(--radius); background: linear-gradient(180deg, var(--panel-2), var(--panel)); }
    .metric-label { color: var(--muted); font-size: 12px; font-weight: 600; }
    .metric-value { font-size: 24px; font-weight: 700; letter-spacing: -0.02em; }
    .metric-hint { color: var(--soft); font-size: 11.5px; }
    .metric.good .metric-value { color: var(--green); }
    .metric.warn .metric-value { color: var(--amber); }
    .metric.bad .metric-value { color: var(--red); }

    .kv-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 8px 24px; }
    .kv { display: flex; justify-content: space-between; gap: 12px; padding: 7px 0; border-bottom: 1px dashed var(--line); font-size: 13px; }
    .kv-label { color: var(--muted); flex: 0 0 auto; }
    .kv-value { text-align: right; overflow-wrap: anywhere; }
    .bar-label { display: flex; justify-content: space-between; margin-bottom: 6px; color: var(--muted); font-size: 12px; }
    .bar { height: 8px; border-radius: 8px; background: var(--bg-2); overflow: hidden; }
    .bar-fill { height: 100%; border-radius: 8px; background: var(--green); transition: width .3s ease; }
    .bar-fill.warn { background: var(--amber); }
    .bar-fill.bad { background: var(--red); }
    .bar-fill.accent { background: var(--accent); }
    .bar-row { display: grid; grid-template-columns: 130px 1fr 60px; align-items: center; gap: 12px; font-size: 13px; }
    .bar-row-label { color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .bar-row-value { text-align: right; font-weight: 600; }
    .switch-row, .action-row { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 10px 0; border-bottom: 1px dashed var(--line); }
    .switch-row:last-child, .action-row:last-child { border-bottom: 0; }
    .danger-card { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 14px 16px; border: 1px solid rgba(242, 109, 106, 0.35); border-radius: var(--radius-sm); background: var(--red-soft); }
    .danger-zone { border-color: rgba(242, 109, 106, 0.4); }

    /* ------------------------------------------------------------ tables */
    .table-wrap { overflow: auto; max-height: 640px; }
    .table-wrap.short { max-height: 360px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 10px 14px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: middle; font-size: 13px; }
    th { position: sticky; top: 0; z-index: 1; background: var(--panel-2); color: var(--soft); font-size: 11px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; white-space: nowrap; }
    tbody tr:last-child td { border-bottom: 0; }
    tbody tr.clickable { cursor: pointer; }
    tbody tr.clickable:hover { background: var(--panel-2); }
    tbody tr.selected { background: var(--accent-soft); }
    tbody tr.selected td:first-child { box-shadow: inset 3px 0 0 var(--accent); }
    tbody tr.dirty { background: rgba(240, 180, 90, 0.06); }
    tbody tr.row-bad { background: rgba(242, 109, 106, 0.06); }
    td.row-actions { text-align: right; white-space: nowrap; }
    td.row-actions .btn + .btn { margin-left: 4px; }
    .w-xs { width: 44px; }
    .w-sm { width: 80px; }
    .w-uid { width: 96px; }
    .w-md { width: 150px; }
    .w-lg { width: 210px; }
    .w-actions { width: 1%; }
    .table-meta { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 18px; border-bottom: 1px solid var(--line); flex-wrap: wrap; }
    .pager { display: flex; align-items: center; gap: 8px; }
    .toolbar { display: flex; align-items: center; gap: 10px; padding: 14px 18px; border-bottom: 1px solid var(--line); flex-wrap: wrap; }
    .toolbar input[type="search"] { flex: 1 1 200px; min-width: 160px; }
    .toolbar select { width: auto; min-width: 150px; }
    .toolbar .btn { flex: 0 0 auto; }
    .hotfix-table input { min-width: 180px; }
    .attachments input { min-width: 64px; }

    /* ------------------------------------------------------------- forms */
    .form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px 16px; }
    .form-grid .wide, .form-grid .actions { grid-column: 1 / -1; }
    .field { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
    .field > span { color: var(--muted); font-size: 12.5px; font-weight: 600; }
    .field.compact { gap: 4px; }
    .field.grow { flex: 1; }
    .input-row { display: flex; align-items: center; gap: 8px; }
    .input-row > input, .input-row > select { flex: 1; }
    .input-row .btn { flex: 0 0 auto; }
    .field-error { color: var(--red); font-size: 12px; }
    .actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .tabs { display: flex; gap: 2px; padding: 8px 12px 0; border-bottom: 1px solid var(--line); overflow-x: auto; }
    .tab { padding: 10px 14px; border-bottom: 2px solid transparent; color: var(--muted); font-weight: 600; white-space: nowrap; }
    .tab:hover { color: var(--ink); }
    .tab.active { color: var(--ink); border-bottom-color: var(--accent); }
    .tab.tab-danger.active { border-bottom-color: var(--red); color: var(--red); }
    .panel.editor { position: sticky; top: calc(var(--topbar) + 16px); }

    /* ---------------------------------------------------------- settings */
    .settings-layout { display: grid; grid-template-columns: 220px minmax(0, 1fr); gap: 18px; align-items: start; }
    .settings-nav { position: sticky; top: calc(var(--topbar) + 16px); display: flex; flex-direction: column; gap: 2px; padding: 8px; border: 1px solid var(--line); border-radius: var(--radius); background: var(--panel); }
    .settings-nav-item { display: flex; align-items: center; justify-content: space-between; padding: 9px 12px; border-radius: 9px; color: var(--muted); font-weight: 600; text-align: left; }
    .settings-nav-item:hover { background: var(--panel-2); color: var(--ink); }
    .settings-nav-item.active { background: var(--accent-soft); color: var(--ink); }
    .settings-nav-item .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--amber); }
    .settings-fields { display: flex; flex-direction: column; }
    .setting { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr); gap: 8px 24px; padding: 14px 0; border-bottom: 1px solid var(--line); }
    .setting:last-child { border-bottom: 0; }
    .setting.dirty { box-shadow: inset 3px 0 0 var(--amber); padding-left: 12px; }
    .setting.has-error { box-shadow: inset 3px 0 0 var(--red); padding-left: 12px; }
    .setting-label { display: flex; flex-direction: column; gap: 4px; }
    .setting-label strong { font-size: 13.5px; }
    .setting-label .chip { align-self: flex-start; }
    .setting-control { display: flex; flex-direction: column; gap: 6px; }

    /* -------------------------------------------------------------- roles */
    .role-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 18px; }
    .role-card.dirty { border-color: rgba(240, 180, 90, 0.5); }

    /* ------------------------------------------------------------ console */
    .console-output { height: 420px; padding: 12px 14px; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--bg); overflow: auto; font-family: var(--mono); font-size: 12.5px; }
    .console-entry { padding: 8px 0; border-bottom: 1px dashed var(--line); }
    .console-entry:last-child { border-bottom: 0; }
    .console-entry.bad .console-result { color: var(--red); }
    .console-line { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; color: var(--accent-2); }
    .console-time { color: var(--soft); }
    .console-prompt { color: var(--green); font-weight: 700; font-family: var(--mono); }
    .console-command { color: var(--ink); font-weight: 600; }
    .console-result { margin-top: 4px; color: var(--muted); }
    #cmd-input { font-family: var(--mono); }
    .catalog-list { max-height: 320px; overflow: auto; border: 1px solid var(--line); border-radius: var(--radius-sm); }
    .catalog-item { display: grid; grid-template-columns: 110px 1fr auto; align-items: center; gap: 10px; width: 100%; padding: 9px 12px; border-bottom: 1px solid var(--line); text-align: left; color: var(--ink); }
    .catalog-item:last-child { border-bottom: 0; }
    .catalog-item:hover { background: var(--panel-2); }
    .catalog-item.active { background: var(--accent-soft); }
    .catalog-item .muted { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .command-detail { display: flex; flex-direction: column; gap: 10px; padding: 14px; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--bg-2); }
    .usage { padding: 10px 12px; border-radius: 7px; background: var(--bg); color: var(--muted); font-size: 12px; }

    /* --------------------------------------------------------------- logs */
    .log-panel .toolbar { gap: 12px; }
    .log-lines { height: calc(100vh - 340px); min-height: 320px; padding: 8px 0; overflow: auto; background: var(--bg); font-family: var(--mono); font-size: 12.5px; border-radius: 0 0 var(--radius) var(--radius); }
    .log-line { display: grid; grid-template-columns: 76px 52px 130px 1fr; gap: 10px; padding: 2px 14px; line-height: 1.5; }
    .log-line:hover { background: var(--panel); }
    .log-time { color: var(--soft); }
    .log-level { font-weight: 700; }
    .log-module { color: var(--cyan); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .log-message { white-space: pre-wrap; overflow-wrap: anywhere; color: var(--ink); }
    .lvl-INFO .log-level { color: var(--accent-2); }
    .lvl-WARN .log-level { color: var(--amber); }
    .lvl-WARN .log-message { color: #8a5815; }
    .lvl-ERROR .log-level, .lvl-FATAL .log-level { color: var(--red); }
    .lvl-ERROR .log-message, .lvl-FATAL .log-message { color: #b93636; }
    .lvl-DEBUG .log-level { color: var(--purple); }
    .lvl-DEBUG .log-message { color: var(--muted); }
    .segmented button.lvl-INFO.active { color: var(--accent-2); }
    .segmented button.lvl-WARN.active { color: var(--amber); }
    .segmented button.lvl-ERROR.active, .segmented button.lvl-FATAL.active { color: var(--red); }
    .segmented button.lvl-DEBUG.active { color: var(--purple); }

    /* --------------------------------------------------------------- mail */
    .mail-list { display: flex; flex-direction: column; gap: 10px; max-height: 520px; overflow: auto; }
    .mail-item { padding: 12px 14px; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--bg-2); }
    .mail-item.read { opacity: .75; }
    .mail-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .mail-content { margin: 8px 0; color: var(--muted); white-space: pre-wrap; font-size: 13px; }
    .finder-list { display: flex; flex-direction: column; max-height: 300px; overflow: auto; border: 1px solid var(--line); border-radius: var(--radius-sm); }
    .finder-item { display: grid; grid-template-columns: 90px 1fr; gap: 10px; padding: 8px 12px; border-bottom: 1px solid var(--line); text-align: left; color: var(--ink); }
    .finder-item:last-child { border-bottom: 0; }
    .finder-item:hover { background: var(--accent-soft); }
    .handbook-text { max-height: 640px; overflow: auto; padding: 14px 18px; color: var(--muted); font-size: 12.5px; }

    /* -------------------------------------------------------------- warps */
    .warp-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
    .warp-grid.compact { grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); }
    .warp-card { display: flex; flex-direction: column; gap: 8px; min-width: 0; padding: 12px 14px; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--panel-2); text-align: left; }
    .warp-card.idle { border-style: dashed; background: var(--panel); }
    .warp-card-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; }
    .warp-card-actions { display: flex; gap: 4px; flex: 0 0 auto; }
    .warp-card-actions .btn { min-height: 26px; padding: 0 7px; font-size: 15px; }
    .warp-card-actions svg { width: 14px; height: 14px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .warp-featured { display: flex; flex-direction: column; gap: 3px; overflow-wrap: anywhere; }
    .warp-star { color: var(--amber); font-weight: 700; }
    .warp-add { align-items: center; justify-content: center; min-height: 150px; border: 1.5px dashed var(--line-2); background: transparent; color: var(--muted); font-weight: 600; text-align: center; }
    .warp-add:hover { border-color: var(--accent); background: var(--accent-soft); color: var(--accent-2); }
    .warp-add-plus { font-size: 34px; font-weight: 300; line-height: 1; }
    .warp-draft-notice { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .warp-pick-tools { display: flex; gap: 8px; flex-wrap: wrap; }
    .warp-pick-tools input { flex: 1 1 220px; width: auto; }
    .warp-pick-tools select { flex: 0 0 auto; width: auto; min-width: 140px; }
    .warp-pick-list { display: flex; flex-direction: column; max-height: 46vh; overflow: auto; border: 1px solid var(--line); border-radius: var(--radius-sm); }
    .warp-pick-item { display: grid; grid-template-columns: 52px minmax(0, 1fr) auto; align-items: center; gap: 10px; width: 100%; padding: 8px 12px; border-bottom: 1px solid var(--line); color: var(--ink); text-align: left; }
    .warp-pick-item:last-child { border-bottom: 0; }
    .warp-pick-item:hover:not(:disabled) { background: var(--accent-soft); }
    .warp-pick-item:disabled { cursor: default; opacity: .6; }
    .warp-pick-main { display: flex; flex-direction: column; gap: 3px; min-width: 0; }

    /* ------------------------------------------------------------- toasts */
    .toast-host { position: fixed; right: 18px; bottom: 18px; z-index: 60; display: flex; flex-direction: column; gap: 8px; max-width: min(420px, calc(100vw - 36px)); }
    .toast { padding: 11px 14px; border: 1px solid var(--line-2); border-radius: var(--radius-sm); background: var(--panel-2); color: var(--ink); font-size: 13px; box-shadow: var(--shadow); opacity: 0; transform: translateY(8px); transition: opacity .2s ease, transform .2s ease; cursor: pointer; overflow-wrap: anywhere; }
    .toast.show { opacity: 1; transform: none; }
    .toast.good { border-color: rgba(62, 207, 142, 0.5); }
    .toast.bad { border-color: rgba(204, 69, 69, 0.45); background: #fff0f0; }

    /* -------------------------------------------------------------- modal */
    .modal-backdrop { position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; padding: 20px; background: rgba(24, 34, 48, 0.28); backdrop-filter: blur(4px); }
    .modal { width: min(100%, 460px); padding: 22px; border: 1px solid var(--line-2); border-radius: 16px; background: var(--panel); box-shadow: var(--shadow); display: flex; flex-direction: column; gap: 14px; }
    .modal.wide { width: min(100%, 760px); max-height: calc(100vh - 40px); overflow-y: auto; }
    .modal-message { color: var(--muted); font-size: 13.5px; white-space: pre-wrap; }
    .modal-actions { display: flex; justify-content: flex-end; gap: 8px; }

    .wrap { overflow-wrap: anywhere; white-space: normal; min-width: 0; }
    #acc-results input[type=checkbox] { width: 16px; height: 16px; cursor: pointer; }
    #block-results td { max-width: 260px; }
    .modal-message { max-height: 45vh; overflow-y: auto; overflow-wrap: anywhere; }

    /* -------------------------------------------------------- responsive */
    @media (max-width: 1180px) {
      .split, .console-split { grid-template-columns: minmax(0, 1fr); }
      .panel.editor { position: static; }
      .settings-layout { grid-template-columns: minmax(0, 1fr); }
      .settings-nav { position: static; flex-direction: row; flex-wrap: wrap; }
    }
    @media (max-width: 900px) {
      .grid.two { grid-template-columns: minmax(0, 1fr); }
      .sidebar { position: fixed; left: 0; top: var(--topbar); z-index: 40; transform: translateX(-100%); transition: transform .2s ease; box-shadow: var(--shadow); }
      .sidebar.open { transform: none; }
      .content { padding: 16px 14px 60px; }
      .form-grid { grid-template-columns: minmax(0, 1fr); }
      .setting { grid-template-columns: minmax(0, 1fr); }
      .log-line { grid-template-columns: 70px 50px 1fr; }
      .log-line .log-message { grid-column: 1 / -1; }
      .topbar .brand span, .topbar #clock { display: none; }
      .bar-row { grid-template-columns: 90px 1fr 50px; }
    }
    @media (max-width: 820px) {
      input, select, textarea { font-size: 16px; }
    }
""";
    }
}
