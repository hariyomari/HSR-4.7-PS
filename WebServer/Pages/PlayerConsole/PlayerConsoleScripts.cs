namespace March7thHoney.WebServer.Pages;

/// <summary>
///     Shared console helpers; tab partials are concatenated into the same closure.
///     Runtime configuration comes from <c>window.M7_CONSOLE_CONFIG</c>, not render-time templating.
/// </summary>
public static partial class PlayerConsoleScripts
{
    public static string Render()
    {
        return string.Concat(
            Core(),
            QuickView(),
            ConsoleView(),
            ReferenceView(),
            HandbookView(),
            Boot());
    }

    private static string Boot()
    {
        return """

      init();
    })();
""";
    }

    private static string Core()
    {
        return """
    (() => {
      "use strict";

      const config = window.M7_CONSOLE_CONFIG || {};
      const I18N = window.M7_CONSOLE_I18N || {};
      const STORAGE = { lang: "m7-console-lang", tab: "m7-console-tab", history: "m7-console-history", login: "m7-console-login" };
      const TOKEN_KEY = "m7-console-token";

      const state = {
        token: readSession(TOKEN_KEY) || "",
        lang: resolveLanguage(),
        player: null,
        catalog: [],
        activeTab: null,
        pending: null
      };

      const views = [];

      function registerView(view) {
        views.push(view);
      }

      function findView(id) {
        return views.find(view => view.id === id) || null;
      }

      // ------------------------------------------------------------ storage
      function readStorage(key) {
        try { return window.localStorage.getItem(key); } catch { return null; }
      }

      function writeStorage(key, value) {
        try {
          if (value === null || value === undefined) window.localStorage.removeItem(key);
          else window.localStorage.setItem(key, value);
        } catch {
        }
      }

      // The session token lives in sessionStorage: a refresh keeps you signed in, closing the tab does not.
      function readSession(key) {
        try { return window.sessionStorage.getItem(key); } catch { return null; }
      }

      function writeSession(key, value) {
        try {
          if (value === null || value === undefined) window.sessionStorage.removeItem(key);
          else window.sessionStorage.setItem(key, value);
        } catch {
        }
      }

      // --------------------------------------------------------------- i18n
      function resolveLanguage() {
        const stored = readStorage(STORAGE.lang);
        if (stored && I18N[stored]) return stored;
        const server = String(config.language || "").toUpperCase();
        if (server === "CHS" || server === "CHT") return "zh-CN";
        return String(navigator.language || "").toLowerCase().startsWith("zh") ? "zh-CN" : "en";
      }

      function t(key, params) {
        const table = I18N[state.lang] || {};
        const fallback = I18N.en || {};
        let text = table[key] ?? fallback[key] ?? key;
        if (params) {
          for (const [name, value] of Object.entries(params)) text = text.split(`{${name}}`).join(String(value ?? ""));
        }
        return text;
      }

      function hasTranslation(key) {
        return (I18N[state.lang] && key in I18N[state.lang]) || (I18N.en && key in I18N.en);
      }

      function setLanguage(lang) {
        if (!I18N[lang]) return;
        state.lang = lang;
        writeStorage(STORAGE.lang, lang);
        document.documentElement.lang = lang;
        renderStaticText();
        if (state.token && state.player) {
          renderShell();
          activateTab(state.activeTab);
        }
      }

      // ------------------------------------------------------------ helpers
      class Raw {
        constructor(value) { this.value = value; }
        toString() { return this.value; }
      }

      function raw(value) { return new Raw(String(value ?? "")); }

      function renderValue(value) {
        if (value === null || value === undefined || value === false) return "";
        if (value instanceof Raw) return value.value;
        if (Array.isArray(value)) return value.map(renderValue).join("");
        return escapeHtml(String(value));
      }

      // Tagged template: interpolations are escaped unless they are Raw (nested html`` results are Raw).
      function html(strings, ...values) {
        let out = "";
        for (let i = 0; i < strings.length; i++) {
          out += strings[i];
          if (i < values.length) out += renderValue(values[i]);
        }
        return new Raw(out);
      }

      function escapeHtml(value) {
        return String(value ?? "")
          .replaceAll("&", "&amp;")
          .replaceAll("<", "&lt;")
          .replaceAll(">", "&gt;")
          .replaceAll('"', "&quot;")
          .replaceAll("'", "&#039;");
      }

      function $(selector, root) { return (root || document).querySelector(selector); }
      function $$(selector, root) { return Array.from((root || document).querySelectorAll(selector)); }

      function on(root, selector, event, handler) {
        for (const element of $$(selector, root)) element.addEventListener(event, handler);
      }

      function debounce(fn, delay) {
        let timer = null;
        return (...args) => {
          clearTimeout(timer);
          timer = setTimeout(() => fn(...args), delay);
        };
      }

      function clamp(value, min, max) { return Math.min(max, Math.max(min, value)); }

      function option(value, label, selected) {
        return html`<option value="${value}" ${String(value) === String(selected) ? "selected" : ""}>${label}</option>`;
      }

      function chip(label, kind) {
        return html`<span class="chip ${kind || ""}">${label}</span>`;
      }

      function kv(label, value) {
        return html`<div class="kv"><span>${label}</span><div>${value}</div></div>`;
      }

      function emptyState(text) { return html`<div class="empty">${text}</div>`; }
      function loadingState(text) { return html`<div class="empty">${text || t("common.loading")}</div>`; }

      function errorMessage(error) {
        return (error && error.message) || String(error || "");
      }

      // ---------------------------------------------------------------- api
      async function api(path, body) {
        const headers = { "Content-Type": "application/json" };
        if (state.token) headers.Authorization = `Bearer ${state.token}`;

        const response = await fetch(path, { method: "POST", headers, body: JSON.stringify(body || {}) });
        let payload = null;
        try { payload = await response.json(); } catch { /* non-JSON error page */ }

        if (response.status === 401 && state.token) {
          clearSession(true);
          throw new Error((payload && payload.message) || t("login.session_expired"));
        }
        if (!payload) throw new Error(`HTTP ${response.status}`);
        if (payload.retcode !== 0) throw new Error(payload.message || `HTTP ${response.status}`);
        return payload;
      }

      /// Runs an async action with the button disabled, toasting the outcome.
      async function run(button, action, successMessage) {
        if (button) button.disabled = true;
        try {
          const result = await action();
          const message = typeof successMessage === "function" ? successMessage(result) : successMessage;
          if (message) toast(message, "good");
          return result;
        } catch (error) {
          toast(errorMessage(error), "bad");
          return null;
        } finally {
          if (button) button.disabled = false;
        }
      }

      function toast(message, kind) {
        const host = $("#toast-host");
        if (!host || !message) return;
        const node = document.createElement("div");
        node.className = `toast ${kind || ""}`;
        node.textContent = message;
        node.addEventListener("click", () => node.remove());
        host.appendChild(node);
        requestAnimationFrame(() => node.classList.add("show"));
        setTimeout(() => {
          node.classList.remove("show");
          setTimeout(() => node.remove(), 250);
        }, kind === "bad" ? 7000 : 4000);
      }

      function confirmDialog({ title, message, confirmLabel, danger }) {
        return new Promise(resolve => {
          const host = $("#modal-host");
          host.innerHTML = String(html`
            <div class="modal-backdrop">
              <div class="modal" role="dialog" aria-modal="true">
                <h2>${title}</h2>
                ${message ? html`<div class="modal-message">${message}</div>` : ""}
                <div class="modal-actions">
                  <button class="btn ghost" type="button" data-modal="cancel">${t("common.cancel")}</button>
                  <button class="btn ${danger ? "danger" : "primary"}" type="button" data-modal="ok">${confirmLabel || t("common.confirm")}</button>
                </div>
              </div>
            </div>`);

          const close = value => {
            host.innerHTML = "";
            document.removeEventListener("keydown", onKey);
            resolve(value);
          };
          const onKey = event => {
            if (event.key === "Escape") close(false);
          };
          document.addEventListener("keydown", onKey);
          $("[data-modal=cancel]", host).addEventListener("click", () => close(false));
          $("[data-modal=ok]", host).addEventListener("click", () => close(true));
          $(".modal-backdrop", host).addEventListener("click", event => {
            if (event.target === event.currentTarget) close(false);
          });
          $("[data-modal=ok]", host).focus();
        });
      }

      async function copyText(value) {
        try {
          await navigator.clipboard.writeText(String(value));
          toast(t("common.copied"), "good");
        } catch {
          toast(t("common.copy_failed"), "bad");
        }
      }

      // ------------------------------------------------------- command model
      // The catalog carries a localized usage blob per command ("Usage: /give <Item id> [x<count>]" lines,
      // one per subcommand). Parsing it once gives the console both its completion source and the argument
      // signature shown under the input.
      const commandModel = {
        byName: new Map(),
        aliases: new Map(),

        rebuild(catalog) {
          this.byName = new Map();
          this.aliases = new Map();
          for (const command of catalog || []) {
            const entry = {
              ...command,
              signatures: parseUsage(command.usage, command.name)
            };
            this.byName.set(command.name.toLowerCase(), entry);
            for (const alias of command.aliases || []) this.aliases.set(String(alias).toLowerCase(), command.name.toLowerCase());
          }
        },

        resolve(name) {
          if (!name) return null;
          const key = String(name).replace(/^\//, "").toLowerCase();
          return this.byName.get(key) || this.byName.get(this.aliases.get(key)) || null;
        },

        allowed(name) {
          const command = this.resolve(name);
          return !command || command.allowed;
        }
      };

      // "Usage: /give <Item id> [x<count>]" / "用法：/give <物品ID> [x<数量=1>]" -> ["/give", "<Item id>", ...]
      function parseUsage(usage, name) {
        const signatures = [];
        for (const line of String(usage || "").split(/\r?\n/)) {
          const trimmed = line.trim();
          if (!trimmed) continue;
          const slash = trimmed.indexOf("/");
          if (slash < 0) continue;
          const body = trimmed.slice(slash);
          if (!body.toLowerCase().startsWith(`/${String(name).toLowerCase()}`)) continue;
          const tokens = splitArgs(body);
          if (tokens.length) signatures.push({ text: body, tokens });
        }
        return signatures;
      }

      // Splits on whitespace but keeps quoted runs and <bracketed args with spaces> together.
      function splitArgs(text) {
        const tokens = [];
        let current = "";
        let quote = "";
        let depth = 0;
        for (const char of String(text || "")) {
          if (quote) {
            current += char;
            if (char === quote) quote = "";
            continue;
          }
          if (char === '"' || char === "'") { quote = char; current += char; continue; }
          if (char === "<" || char === "[") depth++;
          if (char === ">" || char === "]") depth = Math.max(0, depth - 1);
          if (/\s/.test(char) && depth === 0) {
            if (current) { tokens.push(current); current = ""; }
            continue;
          }
          current += char;
        }
        if (current) tokens.push(current);
        return tokens;
      }

      /// Picks the usage line that best matches what has been typed so far, and which argument the caret is on.
      function describeInput(text, caret) {
        const upToCaret = String(text || "").slice(0, caret === undefined ? (text || "").length : caret);
        const tokens = splitArgs(upToCaret);
        const trailingSpace = /\s$/.test(upToCaret);
        const index = trailingSpace ? tokens.length : Math.max(0, tokens.length - 1);
        const command = commandModel.resolve(tokens[0] || "");
        if (!command) return { tokens, index, command: null, signature: null, argIndex: index };

        // Prefer a signature whose literal subcommand tokens match what was typed.
        let best = null;
        let bestScore = -1;
        for (const signature of command.signatures) {
          let score = 0;
          for (let i = 1; i < signature.tokens.length && i < tokens.length; i++) {
            const expected = signature.tokens[i];
            if (expected.startsWith("<") || expected.startsWith("[")) break;
            if (expected.toLowerCase() === String(tokens[i]).toLowerCase()) score += 2;
            else if (!trailingSpace && i === tokens.length - 1 && expected.toLowerCase().startsWith(String(tokens[i]).toLowerCase())) score += 1;
            else { score = -1; break; }
          }
          if (score > bestScore) { bestScore = score; best = signature; }
        }
        if (!best) best = command.signatures[0] || null;
        return { tokens, index, command, signature: best, argIndex: index };
      }

      // Argument placeholders name what they want ("<Item id>" / "<物品ID>"), which is enough to point the
      // completion list at the right handbook section.
      const SECTION_HINTS = [
        { section: "Avatar", patterns: [/avatar/i, /character/i, /角色/, /角色ID/] },
        { section: "Item", patterns: [/item/i, /物品/, /道具/] },
        { section: "StageId", patterns: [/stage/i, /关卡/, /战斗/] },
        { section: "MainMission", patterns: [/mission/i, /quest/i, /任务/] },
        { section: "RogueBuff", patterns: [/buff/i, /祝福/] },
        { section: "RogueMiracle", patterns: [/miracle/i, /curio/i, /奇物/] }
      ];

      function sectionForArgument(commandName, placeholder) {
        const text = String(placeholder || "");
        for (const hint of SECTION_HINTS) {
          if (hint.patterns.some(pattern => pattern.test(text))) return hint.section;
        }
        // Fall back to the command's own subject when the placeholder is generic ("<id>").
        const name = String(commandName || "").toLowerCase();
        if (/^id\b|ID/.test(text.replace(/[<>\[\]]/g, ""))) {
          if (name === "give" || name === "remove") return "Item";
          if (name === "avatar" || name === "lineup" || name === "hero") return "Avatar";
        }
        return null;
      }

      // ------------------------------------------------------------- handbook
      // Lazy loader for the generated GM handbook (names for items/avatars/stages). Same wire format the
      // admin console reads, served from /handbook/*.
      const handbook = {
        cache: {},
        languages: null,
        pending: {},
        defaultLang() {
          const langs = this.languages || [];
          const preferred = state.lang === "zh-CN" ? ["CHS", "CHT", "EN"] : ["EN", "CHS", "CHT"];
          for (const candidate of preferred) {
            const match = langs.find(lang => lang.toUpperCase() === candidate);
            if (match) return match;
          }
          return langs[0] || null;
        },
        async loadLanguages() {
          if (this.languages) return this.languages;
          try {
            const response = await fetch("/handbook/languages");
            const payload = await response.json();
            this.languages = payload.languages || [];
          } catch {
            this.languages = [];
          }
          return this.languages;
        },
        async load(lang) {
          if (this.cache[lang]) return this.cache[lang];
          if (this.pending[lang]) return this.pending[lang];
          this.pending[lang] = (async () => {
            const response = await fetch(`/handbook/content?lang=${encodeURIComponent(lang)}`);
            if (!response.ok) throw new Error(t("handbook.load_failed"));
            const parsed = this.parse(await response.text());
            this.cache[lang] = parsed;
            delete this.pending[lang];
            return parsed;
          })();
          return this.pending[lang];
        },
        // The generator writes the sections in this fixed order with localized headings ("#Item" / "#物品"),
        // so the i-th heading is mapped onto a stable id and the localized text is kept as the title.
        canonicalSections: ["Command", "Avatar", "Item", "StageId", "MainMission", "SubMission", "RogueBuff",
          "RogueMiracle", "CurrencyWarRole", "CurrencyWarEquipment", "CurrencyWarConsumable", "CurrencyWarOrb",
          "RogueDiceSurface", "RogueDialogue"],
        parse(text) {
          const sections = {};
          const order = [];
          let current = null;
          let headingIndex = -1;
          for (const line of text.split(/\r?\n/)) {
            if (line.startsWith("#")) {
              const name = line.slice(1).trim();
              if (!name) continue;
              headingIndex++;
              if (headingIndex === 0 && /\d{4}/.test(name)) {
                // "#Handbook generated in ..." / "#手册生成时间 ..." carries no entries.
                headingIndex--;
                continue;
              }
              current = this.canonicalSections[headingIndex] || name;
              if (!sections[current]) {
                sections[current] = { title: name, entries: [], index: new Map(), lines: [] };
                order.push(current);
              }
              continue;
            }
            if (!current) continue;
            const section = sections[current];
            const match = /^\s*(\d+)\s*:\s*(.*)$/.exec(line);
            if (match) {
              const entry = { id: Number(match[1]), text: match[2].trim() };
              section.entries.push(entry);
              if (!section.index.has(entry.id)) section.index.set(entry.id, entry.text);
            } else if (line.trim()) {
              section.lines.push(line);
            }
          }
          return { sections, order };
        },
        book() {
          const lang = this.defaultLang();
          return lang ? this.cache[lang] : null;
        },
        async ensureDefault() {
          await this.loadLanguages();
          const lang = this.defaultLang();
          if (!lang) return null;
          try { return await this.load(lang); } catch { return null; }
        },
        entries(section) {
          return this.book()?.sections[section]?.entries || [];
        },
        name(section, id) {
          return this.book()?.sections[section]?.index.get(Number(id)) || "";
        },
        /// Best-effort id lookup: accepts an id, or a name the player typed instead of an id.
        search(section, query, limit) {
          const entries = this.entries(section);
          if (!entries.length) return [];
          const text = String(query || "").trim().toLowerCase();
          if (!text) return entries.slice(0, limit || 20);
          const matches = entries.filter(entry =>
            String(entry.id).startsWith(text) || entry.text.toLowerCase().includes(text));
          return matches.slice(0, limit || 20);
        },
        resolveId(section, value) {
          const text = String(value || "").trim();
          if (!text) return "";
          if (/^-?\d+$/.test(text)) return text;
          const match = this.search(section, text, 1)[0];
          return match ? String(match.id) : text;
        }
      };

      // -------------------------------------------------------------- shell
      const els = {};

      function cacheElements() {
        els.loginView = $("#login-view");
        els.loginForm = $("#login-form");
        els.loginUid = $("#login-uid");
        els.loginPassword = $("#login-password");
        els.loginToggle = $("#login-toggle");
        els.loginRemember = $("#login-remember");
        els.loginMessage = $("#login-message");
        els.app = $("#app");
        els.tabs = $("#tabs");
        els.viewRoot = $("#view-root");
        els.playerChip = $("#player-chip");
        els.statusChip = $("#status-chip");
        els.refreshButton = $("#refresh-btn");
        els.logoutButton = $("#logout-btn");
      }

      const ICONS = {
        quick: '<svg viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M11 2 4 11h5l-1 7 7-9h-5z"/></svg>',
        console: '<svg viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="3.5" width="15" height="13" rx="2"/><path d="m6 8 2.5 2L6 12M11 12.5h3"/></svg>',
        reference: '<svg viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4.5A1.5 1.5 0 0 1 5.5 3H16v14H5.5A1.5 1.5 0 0 1 4 15.5z"/><path d="M7 7h6M7 10h6"/></svg>',
        handbook: '<svg viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="9" r="5.5"/><path d="m13.5 13.5 3.5 3.5"/></svg>'
      };

      function renderStaticText() {
        for (const element of $$("[data-i18n]")) element.textContent = t(element.dataset.i18n);
        for (const element of $$("[data-lang]")) element.classList.toggle("active", element.dataset.lang === state.lang);
        els.loginToggle.textContent = els.loginPassword.type === "password" ? t("login.show") : t("login.hide");
        document.title = `${t("app.title")} - ${config.serverName || "March7thHoney"}`;
      }

      function renderShell() {
        const player = state.player;
        if (!player) return;
        els.playerChip.textContent = t("shell.player_chip", { uid: player.uid, role: player.role });
        els.statusChip.textContent = player.online ? t("shell.online") : t("shell.offline");
        els.statusChip.className = `chip ${player.online ? "good" : "warn"}`;

        els.tabs.innerHTML = String(html`${views.map(view => html`
          <button type="button" class="tab ${view.id === state.activeTab ? "active" : ""}" data-tab="${view.id}">
            ${raw(ICONS[view.id] || "")}<span>${t(`tab.${view.id}`)}</span>
          </button>`)}`);
        on(els.tabs, "[data-tab]", "click", event => activateTab(event.currentTarget.dataset.tab));
      }

      function activateTab(id) {
        const view = findView(id) || views[0];
        if (!view) return;
        state.activeTab = view.id;
        writeStorage(STORAGE.tab, view.id);
        for (const button of $$("[data-tab]", els.tabs)) button.classList.toggle("active", button.dataset.tab === view.id);
        els.viewRoot.innerHTML = String(loadingState());

        const token = Symbol("render");
        state.pending = token;
        Promise.resolve(view.load ? view.load() : null)
          .then(() => {
            if (state.pending !== token) return;
            view.render(els.viewRoot);
          })
          .catch(error => {
            if (state.pending !== token) return;
            els.viewRoot.innerHTML = String(html`<section class="panel"><div class="empty bad">${errorMessage(error)}</div></section>`);
          });
      }

      /// Shared entry point for every "run this command" button on the page. Successes and failures both
      /// land in the console log, so the transcript is the whole story no matter which tab started it.
      async function execute(command, button) {
        const text = String(command || "").trim();
        if (!text) {
          toast(t("console.empty"), "bad");
          return null;
        }
        if (!state.player?.online) {
          toast(t("console.offline"), "bad");
          pushConsoleEntry(text, t("console.offline"), false);
          return null;
        }
        if (button) button.disabled = true;
        try {
          const payload = await api("/player/exec", { command: text });
          pushConsoleEntry(text, payload.output || t("console.no_result"), true);
          return payload;
        } catch (error) {
          pushConsoleEntry(text, errorMessage(error), false);
          toast(errorMessage(error), "bad");
          return null;
        } finally {
          if (button) button.disabled = false;
        }
      }

      // ------------------------------------------------------------- session
      async function refreshPlayer() {
        const payload = await api("/player/session", {});
        state.player = payload.data;
        renderShell();
        return state.player;
      }

      async function loadCatalog() {
        const payload = await api("/player/catalog", {});
        state.catalog = payload.data.commands || [];
        commandModel.rebuild(state.catalog);
        state.catalogMeta = { role: payload.data.role, allowed: payload.data.allowed_count, count: payload.data.count };
        return state.catalog;
      }

      async function handleLogin(event) {
        event.preventDefault();
        const uid = els.loginUid.value.trim();
        if (!/^\d+$/.test(uid)) {
          setLoginMessage(t("login.uid_required"), "bad");
          return;
        }
        setLoginMessage(t("login.signing_in"), "");
        const button = $("button[type=submit]", els.loginForm);
        button.disabled = true;
        try {
          const payload = await api("/player/login", { username: uid, password: els.loginPassword.value });
          state.token = payload.data.token;
          state.player = payload.data.player;
          writeSession(TOKEN_KEY, state.token);
          writeStorage(STORAGE.login, els.loginRemember.checked
            ? JSON.stringify({ uid, password: els.loginPassword.value })
            : null);
          els.loginPassword.value = "";
          setLoginMessage("", "");
          await enterWorkspace();
        } catch (error) {
          setLoginMessage(errorMessage(error), "bad");
        } finally {
          button.disabled = false;
        }
      }

      async function enterWorkspace() {
        els.loginView.classList.add("hidden");
        els.app.classList.remove("hidden");
        try {
          await loadCatalog();
        } catch (error) {
          toast(errorMessage(error), "bad");
        }
        handbook.ensureDefault();
        renderShell();
        const stored = readStorage(STORAGE.tab);
        activateTab(findView(stored) ? stored : views[0]?.id);
      }

      async function restoreSession() {
        try {
          await refreshPlayer();
          await enterWorkspace();
        } catch {
          clearSession(false);
        }
      }

      async function handleLogout() {
        try { await api("/player/logout", {}); } catch { /* the token is dropped either way */ }
        clearSession(false);
        toast(t("login.signed_out"), "");
      }

      function clearSession(expired) {
        state.token = "";
        state.player = null;
        state.catalog = [];
        writeSession(TOKEN_KEY, null);
        fillRememberedLogin();
        els.app.classList.add("hidden");
        els.loginView.classList.remove("hidden");
        setLoginMessage(expired ? t("login.session_expired") : "", expired ? "bad" : "");
        setTimeout(() => els.loginUid.focus(), 50);
      }

      function setLoginMessage(text, kind) {
        els.loginMessage.textContent = text;
        els.loginMessage.className = `message ${kind || ""}`;
      }

      // Saved credentials are only written after a successful sign-in with "remember" ticked.
      function fillRememberedLogin() {
        let saved = null;
        try { saved = JSON.parse(readStorage(STORAGE.login) || "null"); } catch { saved = null; }
        if (!saved || typeof saved.uid !== "string") return;
        els.loginUid.value = saved.uid;
        els.loginPassword.value = typeof saved.password === "string" ? saved.password : "";
        els.loginRemember.checked = true;
      }

      // ---------------------------------------------------------------- init
      function init() {
        cacheElements();
        document.documentElement.lang = state.lang;
        renderStaticText();

        els.loginForm.addEventListener("submit", handleLogin);
        els.loginToggle.addEventListener("click", () => {
          const hidden = els.loginPassword.type === "password";
          els.loginPassword.type = hidden ? "text" : "password";
          els.loginToggle.textContent = hidden ? t("login.hide") : t("login.show");
        });
        els.logoutButton.addEventListener("click", handleLogout);
        els.refreshButton.addEventListener("click", async event => {
          await run(event.currentTarget, async () => {
            await refreshPlayer();
            await loadCatalog();
            activateTab(state.activeTab);
          });
        });
        on(document, "[data-lang]", "click", event => setLanguage(event.currentTarget.dataset.lang));
        fillRememberedLogin();

        if (state.token) restoreSession();
        else setTimeout(() => els.loginUid.focus(), 50);
      }

""";
    }
}
