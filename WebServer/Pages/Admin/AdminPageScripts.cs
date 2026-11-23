namespace March7thHoney.WebServer.Pages;

/// <summary>
///     Client script for the admin console. The core (state, i18n, API client, router, UI primitives) lives in this
///     file; every feature module is a partial next to it and is concatenated inside the same closure, so modules
///     can use the core helpers directly. Nothing here is templated at render time — the page passes runtime
///     configuration through <c>window.M7_ADMIN_CONFIG</c>.
/// </summary>
public static partial class AdminPageScripts
{
    public static string Render()
    {
        return string.Concat(
            Core(),
            OverviewModule(),
            AccountsModule(),
            PlayerEditorModule(),
            IdentityBlocksModule(),
            SessionsModule(),
            GridFightModule(),
            MailModule(),
            AnnounceModule(),
            AccessModule(),
            CommandsModule(),
            SettingsModule(),
            WarpsModule(),
            HotfixModule(),
            LogsModule(),
            MaintenanceModule(),
            HandbookModule(),
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

      const config = window.M7_ADMIN_CONFIG || {};
      const I18N = window.M7_ADMIN_I18N || {};
      const STORAGE = {
        token: "m7-admin-token",
        lang: "m7-admin-lang",
        module: "m7-admin-module",
        sidebar: "m7-admin-sidebar",
        history: "m7-admin-cmd-history"
      };

      // ---------------------------------------------------------------- state
      const state = {
        token: readStorage(STORAGE.token) || "",
        lang: resolveLanguage(),
        session: null,
        activeModule: null,
        moduleParams: {},
        pollTimer: null,
        clockTimer: null,
        cache: {}
      };

      const modules = [];
      const moduleGroups = [
        { id: "overview", modules: ["overview"] },
        { id: "players", modules: ["accounts", "editor", "blocks", "sessions", "gridfight", "mail", "announce"] },
        { id: "server", modules: ["commands", "access", "settings", "warps", "hotfix"] },
        { id: "ops", modules: ["logs", "maintenance", "handbook"] }
      ];

      function registerModule(module) {
        modules.push(module);
      }

      function findModule(id) {
        return modules.find(module => module.id === id) || null;
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

      // --------------------------------------------------------------- i18n
      function resolveLanguage() {
        const stored = readStorage(STORAGE.lang);
        if (stored && I18N[stored]) return stored;
        const server = String(config.language || "").toUpperCase();
        if (server === "CHS" || server === "CHT") return "zh-CN";
        const browser = String(navigator.language || "").toLowerCase();
        if (browser.startsWith("zh")) return "zh-CN";
        return "en";
      }

      function t(key, params) {
        const table = I18N[state.lang] || {};
        const fallback = I18N.en || {};
        let text = table[key] ?? fallback[key] ?? key;
        if (params) {
          for (const [name, value] of Object.entries(params)) {
            text = text.split(`{${name}}`).join(String(value ?? ""));
          }
        }
        return text;
      }

      function setLanguage(lang) {
        if (!I18N[lang]) return;
        state.lang = lang;
        writeStorage(STORAGE.lang, lang);
        document.documentElement.lang = lang;
        renderShellText();
        renderNav();
        if (state.token && state.activeModule) {
          activateModule(state.activeModule, state.moduleParams, { keepCache: true });
        } else {
          renderLoginText();
        }
      }

      // ------------------------------------------------------------ helpers
      class Raw {
        constructor(value) { this.value = value; }
        toString() { return this.value; }
      }

      function raw(value) {
        return new Raw(String(value ?? ""));
      }

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

      function $(selector, root) {
        return (root || document).querySelector(selector);
      }

      function $$(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
      }

      function on(root, selector, event, handler) {
        for (const element of $$(selector, root)) {
          element.addEventListener(event, handler);
        }
      }

      function debounce(fn, delay) {
        let timer = null;
        return (...args) => {
          clearTimeout(timer);
          timer = setTimeout(() => fn(...args), delay);
        };
      }

      function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
      }

      const fmt = {
        number(value) {
          return new Intl.NumberFormat(state.lang).format(Number(value) || 0);
        },
        unix(value) {
          const timestamp = Number(value) || 0;
          if (timestamp <= 0) return t("common.none");
          return new Date(timestamp * 1000).toLocaleString(state.lang);
        },
        unixMs(value) {
          const timestamp = Number(value) || 0;
          if (timestamp <= 0) return t("common.none");
          return new Date(timestamp).toLocaleTimeString(state.lang, { hour12: false });
        },
        relative(value) {
          const timestamp = Number(value) || 0;
          if (timestamp <= 0) return t("common.never");
          const delta = Math.round(Date.now() / 1000 - timestamp);
          const abs = Math.abs(delta);
          const suffix = delta >= 0 ? "common.ago" : "common.from_now";
          if (abs < 60) return t(suffix, { value: t("common.seconds", { count: abs }) });
          if (abs < 3600) return t(suffix, { value: t("common.minutes", { count: Math.floor(abs / 60) }) });
          if (abs < 86400) return t(suffix, { value: t("common.hours", { count: Math.floor(abs / 3600) }) });
          return t(suffix, { value: t("common.days", { count: Math.floor(abs / 86400) }) });
        },
        duration(seconds) {
          seconds = Math.max(0, Math.floor(Number(seconds) || 0));
          const days = Math.floor(seconds / 86400);
          const hours = Math.floor((seconds % 86400) / 3600);
          const minutes = Math.floor((seconds % 3600) / 60);
          const secs = seconds % 60;
          if (days > 0) return `${days}d ${hours}h`;
          if (hours > 0) return `${hours}h ${minutes}m`;
          if (minutes > 0) return `${minutes}m ${secs}s`;
          return `${secs}s`;
        },
        bytes(value) {
          let size = Number(value) || 0;
          const units = ["B", "KB", "MB", "GB"];
          let index = 0;
          while (size >= 1024 && index < units.length - 1) {
            size /= 1024;
            index++;
          }
          return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
        },
        mb(value) {
          return `${fmt.number(Math.round(Number(value) || 0))} MB`;
        }
      };

      function unixToDateTimeLocal(value) {
        const date = new Date(Number(value) * 1000);
        const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
        return local.toISOString().slice(0, 16);
      }

      function dateTimeLocalToUnix(value) {
        if (!value) return null;
        const timestamp = new Date(value).getTime();
        return Number.isFinite(timestamp) ? Math.floor(timestamp / 1000) : null;
      }

      function parseUidList(text) {
        return Array.from(new Set(String(text || "")
          .split(/[\s,;，]+/)
          .map(value => Number(value))
          .filter(value => Number.isInteger(value) && value > 0)));
      }

      async function copyText(text) {
        try {
          await navigator.clipboard.writeText(String(text));
          toast(t("common.copied"), "good");
        } catch {
          toast(t("common.copy_failed"), "bad");
        }
      }

      function downloadBlob(name, blob) {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = name;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
      }

      // ----------------------------------------------------------------- api
      async function api(path, options = {}) {
        const headers = { ...(options.headers || {}) };
        if (state.token) headers.Authorization = `Bearer ${state.token}`;
        let body = options.body;
        if (body !== undefined && body !== null && typeof body !== "string") {
          body = JSON.stringify(body);
        }
        if (body !== undefined && !headers["Content-Type"]) headers["Content-Type"] = "application/json";

        let url = path;
        if (options.query) {
          const params = new URLSearchParams();
          for (const [key, value] of Object.entries(options.query)) {
            if (value === undefined || value === null || value === "") continue;
            params.set(key, String(value));
          }
          const query = params.toString();
          if (query) url += (url.includes("?") ? "&" : "?") + query;
        }

        const response = await fetch(url, { method: options.method || "GET", headers, body });
        const payload = await response.json().catch(() => ({ retcode: -1, message: t("common.request_failed") }));
        if (!response.ok || payload.retcode !== 0) {
          if (response.status === 401 && !options.allowUnauthorized) {
            clearSession(true);
          }
          throw new Error(payload.message || t("common.request_failed"));
        }
        return payload.data;
      }

      async function apiBlob(path) {
        const headers = {};
        if (state.token) headers.Authorization = `Bearer ${state.token}`;
        const response = await fetch(path, { headers });
        if (!response.ok) {
          const payload = await response.json().catch(() => ({}));
          throw new Error(payload.message || t("common.request_failed"));
        }
        return response.blob();
      }

      function errorMessage(error) {
        return error instanceof Error ? error.message : String(error || t("common.request_failed"));
      }

      // Runs an async action with a busy state on the triggering button and toast feedback.
      async function run(button, action, successMessage) {
        if (button) {
          button.disabled = true;
          button.classList.add("busy");
        }
        try {
          const result = await action();
          if (successMessage) toast(typeof successMessage === "function" ? successMessage(result) : successMessage, "good");
          return result;
        } catch (error) {
          toast(errorMessage(error), "bad");
          return undefined;
        } finally {
          if (button) {
            button.disabled = false;
            button.classList.remove("busy");
          }
        }
      }

      // -------------------------------------------------------------- toasts
      function toast(message, kind = "", timeout = 4200) {
        const host = $("#toast-host");
        if (!host) return;
        const element = document.createElement("div");
        element.className = `toast ${kind}`;
        element.setAttribute("role", "status");
        element.textContent = message;
        host.appendChild(element);
        requestAnimationFrame(() => element.classList.add("show"));
        const remove = () => {
          element.classList.remove("show");
          setTimeout(() => element.remove(), 250);
        };
        element.addEventListener("click", remove);
        setTimeout(remove, timeout);
      }

      // --------------------------------------------------------------- modal
      // confirmDialog({ title, message, confirmLabel, danger, requireText, input, placeholder }) -> Promise
      function confirmDialog(options = {}) {
        return new Promise(resolve => {
          const host = $("#modal-host");
          const requireText = options.requireText || "";
          const wantsInput = Boolean(options.input) || Boolean(requireText);
          host.innerHTML = String(html`
            <div class="modal-backdrop">
              <div class="modal" role="dialog" aria-modal="true">
                <h3>${options.title || t("common.confirm")}</h3>
                ${options.message ? html`<p class="modal-message">${options.message}</p>` : ""}
                ${wantsInput ? html`
                  <label class="field">
                    <span>${requireText ? t("common.type_to_confirm", { text: requireText }) : (options.inputLabel || "")}</span>
                    <input id="modal-input" autocomplete="off" placeholder="${options.placeholder || ""}" value="${options.value || ""}">
                  </label>` : ""}
                <div class="modal-actions">
                  <button class="btn ghost" type="button" data-modal="cancel">${t("common.cancel")}</button>
                  <button class="btn ${options.danger ? "danger" : "primary"}" type="button" data-modal="ok" ${requireText ? "disabled" : ""}>${options.confirmLabel || t("common.confirm")}</button>
                </div>
              </div>
            </div>`);

          const input = $("#modal-input", host);
          const okButton = $("[data-modal=ok]", host);
          const close = result => {
            host.innerHTML = "";
            document.removeEventListener("keydown", onKey);
            resolve(result);
          };
          const onKey = event => {
            if (event.key === "Escape") close(false);
            if (event.key === "Enter" && !okButton.disabled && document.activeElement !== okButton) {
              event.preventDefault();
              okButton.click();
            }
          };
          document.addEventListener("keydown", onKey);
          $("[data-modal=cancel]", host).addEventListener("click", () => close(false));
          $(".modal-backdrop", host).addEventListener("click", event => {
            if (event.target === event.currentTarget) close(false);
          });
          okButton.addEventListener("click", () => close(wantsInput && !requireText ? (input?.value ?? "") : true));
          if (input) {
            input.addEventListener("input", () => {
              if (requireText) okButton.disabled = input.value.trim() !== requireText;
            });
            input.focus();
          } else {
            okButton.focus();
          }
        });
      }

      // ------------------------------------------------------- UI primitives
      function chip(text, kind = "") {
        return html`<span class="chip ${kind}">${text}</span>`;
      }

      function metric(label, value, options = {}) {
        return html`
          <article class="metric ${options.kind || ""}">
            <span class="metric-label">${label}</span>
            <strong class="metric-value">${value}</strong>
            ${options.hint ? html`<span class="metric-hint">${options.hint}</span>` : ""}
          </article>`;
      }

      function kv(label, value, options = {}) {
        return html`
          <div class="kv">
            <span class="kv-label">${label}</span>
            <span class="kv-value ${options.mono ? "mono" : ""}">${value ?? ""}</span>
          </div>`;
      }

      function emptyState(text) {
        return html`<div class="empty">${text}</div>`;
      }

      function loadingState(text) {
        return html`<div class="empty loading">${text || t("common.loading")}</div>`;
      }

      function option(value, label, selected) {
        return html`<option value="${value}" ${String(value) === String(selected) ? "selected" : ""}>${label}</option>`;
      }

      function toggleSwitch(id, checked, label) {
        return html`
          <label class="switch">
            <input id="${id}" type="checkbox" ${checked ? "checked" : ""}>
            <span class="switch-track"></span>
            ${label ? html`<span class="switch-label">${label}</span>` : ""}
          </label>`;
      }

      function permissionChips(permissions, options = {}) {
        const items = Array.isArray(permissions)
          ? permissions
          : String(permissions || "").split(/[,\s;]+/).filter(Boolean);
        if (!items.length) return html`<span class="chip subtle">${t("common.none")}</span>`;
        return html`<span class="chip-list">${items.map(permission => html`
          <span class="chip ${permission.startsWith("role:") ? "accent" : ""} ${options.removable ? "removable" : ""}" data-permission="${permission}">
            ${permission}
            ${options.removable ? html`<button type="button" class="chip-remove" data-remove-permission="${permission}" aria-label="${t("common.remove")}">&times;</button>` : ""}
          </span>`)}</span>`;
      }

      function paginate(items, page, pageSize) {
        const total = items.length;
        const pages = Math.max(1, Math.ceil(total / pageSize));
        const current = clamp(page, 1, pages);
        return {
          items: items.slice((current - 1) * pageSize, current * pageSize),
          page: current,
          pages,
          total
        };
      }

      function pager(pagination, attr) {
        if (pagination.pages <= 1) return "";
        return html`
          <div class="pager">
            <button class="btn ghost small" type="button" data-${attr}="${pagination.page - 1}" ${pagination.page <= 1 ? "disabled" : ""}>&lsaquo;</button>
            <span class="muted">${t("common.page_of", { page: pagination.page, pages: pagination.pages })}</span>
            <button class="btn ghost small" type="button" data-${attr}="${pagination.page + 1}" ${pagination.page >= pagination.pages ? "disabled" : ""}>&rsaquo;</button>
          </div>`;
      }

      // ------------------------------------------------------------- handbook
      // Lazy loader for the generated GM handbook (names for items/avatars/stages).
      const handbook = {
        cache: {},
        languages: null,
        pending: {},
        defaultLang() {
          const langs = this.languages || [];
          const preferred = state.lang === "zh-CN" ? ["CHS", "CHT", "EN"] : ["EN", "CHS", "CHT"];
          for (const candidate of preferred) {
            if (langs.some(lang => lang.toUpperCase() === candidate)) return langs.find(lang => lang.toUpperCase() === candidate);
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
            const text = await response.text();
            const parsed = this.parse(text);
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
        async ensureDefault() {
          await this.loadLanguages();
          const lang = this.defaultLang();
          if (!lang) return null;
          try {
            return await this.load(lang);
          } catch {
            return null;
          }
        },
        name(section, id) {
          const lang = this.defaultLang();
          const book = lang ? this.cache[lang] : null;
          const entry = book?.sections[section]?.index.get(Number(id));
          return entry || "";
        },
        label(section, id) {
          const name = this.name(section, id);
          return name ? `${name} (${id})` : String(id);
        }
      };

      // ---------------------------------------------------------------- shell
      const els = {};

      function cacheElements() {
        Object.assign(els, {
          loginView: $("#login-view"),
          loginForm: $("#login-form"),
          loginKey: $("#login-key"),
          loginMessage: $("#login-message"),
          loginToggleKey: $("#login-toggle-key"),
          app: $("#app"),
          nav: $("#nav"),
          navToggle: $("#nav-toggle"),
          sidebar: $("#sidebar"),
          moduleRoot: $("#module-root"),
          moduleKicker: $("#module-kicker"),
          moduleTitle: $("#module-title"),
          moduleActions: $("#module-actions"),
          refreshButton: $("#refresh-btn"),
          logoutButton: $("#logout-btn"),
          clock: $("#clock"),
          sessionInfo: $("#session-info")
        });
      }

      function init() {
        cacheElements();
        document.documentElement.lang = state.lang;
        renderShellText();
        renderLoginText();

        els.loginForm.addEventListener("submit", handleLogin);
        els.loginToggleKey.addEventListener("click", () => {
          const hidden = els.loginKey.type === "password";
          els.loginKey.type = hidden ? "text" : "password";
          els.loginToggleKey.textContent = hidden ? t("login.hide") : t("login.show");
        });
        els.logoutButton.addEventListener("click", handleLogout);
        els.refreshButton.addEventListener("click", () => {
          if (state.activeModule) activateModule(state.activeModule, state.moduleParams, { force: true });
        });
        els.navToggle.addEventListener("click", () => {
          els.sidebar.classList.toggle("open");
        });
        on(document, "[data-lang]", "click", event => setLanguage(event.currentTarget.dataset.lang));
        window.addEventListener("hashchange", () => routeFromHash());
        document.addEventListener("visibilitychange", () => {
          if (document.hidden) stopPolling();
          else if (state.activeModule) startPolling(findModule(state.activeModule));
        });

        renderNav();
        if (state.token) {
          restoreSession();
        } else {
          showLogin();
        }
      }

      function renderShellText() {
        for (const element of $$("[data-i18n]")) {
          element.textContent = t(element.dataset.i18n);
        }
        for (const element of $$("[data-i18n-title]")) {
          element.title = t(element.dataset.i18nTitle);
        }
        for (const element of $$("[data-lang]")) {
          element.classList.toggle("active", element.dataset.lang === state.lang);
        }
        document.title = `${t("app.title")} - ${config.serverName || "March7thHoney"}`;
      }

      function renderLoginText() {
        els.loginKey.placeholder = t("login.key_placeholder");
        els.loginToggleKey.textContent = els.loginKey.type === "password" ? t("login.show") : t("login.hide");
      }

      async function restoreSession() {
        try {
          state.session = await api("/admin/api/session", { allowUnauthorized: true });
          showWorkspace();
          routeFromHash();
        } catch {
          clearSession(false);
        }
      }

      async function handleLogin(event) {
        event.preventDefault();
        setLoginMessage(t("login.signing_in"), "");
        const button = $("button[type=submit]", els.loginForm);
        button.disabled = true;
        try {
          const data = await api("/admin/api/login", {
            method: "POST",
            body: { admin_key: els.loginKey.value },
            allowUnauthorized: true
          });
          state.token = data.token;
          writeStorage(STORAGE.token, state.token);
          els.loginKey.value = "";
          state.session = await api("/admin/api/session");
          setLoginMessage("", "");
          showWorkspace();
          routeFromHash();
        } catch (error) {
          setLoginMessage(errorMessage(error), "bad");
        } finally {
          button.disabled = false;
        }
      }

      async function handleLogout() {
        try {
          await api("/admin/api/logout", { method: "POST" });
        } catch {
        }
        clearSession(false);
        toast(t("login.signed_out"), "");
      }

      function setLoginMessage(text, kind) {
        els.loginMessage.textContent = text;
        els.loginMessage.className = `message ${kind}`;
      }

      function showWorkspace() {
        els.loginView.classList.add("hidden");
        els.app.classList.remove("hidden");
        renderSessionInfo();
        startClock();
      }

      function showLogin() {
        stopPolling();
        stopClock();
        els.app.classList.add("hidden");
        els.loginView.classList.remove("hidden");
        setTimeout(() => els.loginKey.focus(), 50);
      }

      function clearSession(expired) {
        state.token = "";
        state.session = null;
        state.cache = {};
        writeStorage(STORAGE.token, null);
        showLogin();
        if (expired) setLoginMessage(t("login.session_expired"), "bad");
      }

      function renderSessionInfo() {
        if (!els.sessionInfo || !state.session) return;
        els.sessionInfo.innerHTML = String(html`
          <div>${t("shell.version")}: <strong>${state.session.version || ""}</strong></div>
          <div>${t("shell.session_expires")}: <strong>${fmt.unix(state.session.expires_at)}</strong></div>`);
      }

      function startClock() {
        stopClock();
        const tick = () => {
          if (els.clock) els.clock.textContent = new Date().toLocaleTimeString(state.lang, { hour12: false });
        };
        tick();
        state.clockTimer = setInterval(tick, 1000);
      }

      function stopClock() {
        if (state.clockTimer) clearInterval(state.clockTimer);
        state.clockTimer = null;
      }

      // ----------------------------------------------------------------- nav
      function renderNav() {
        if (!els.nav) return;
        els.nav.innerHTML = String(html`${moduleGroups.map(group => html`
          <div class="nav-group">
            <p class="nav-group-title">${t(`nav.group.${group.id}`)}</p>
            ${group.modules.map(id => findModule(id)).filter(Boolean).map(module => html`
              <a class="nav-item ${module.id === state.activeModule ? "active" : ""}" href="#/${module.id}" data-module="${module.id}">
                <span class="nav-icon" aria-hidden="true">${raw(module.icon || "")}</span>
                <span class="nav-text">
                  <strong>${t(`nav.${module.id}`)}</strong>
                  <span>${t(`nav.${module.id}.summary`)}</span>
                </span>
              </a>`)}
          </div>`)}`);
        on(els.nav, "[data-module]", "click", () => els.sidebar.classList.remove("open"));
      }

      function routeFromHash() {
        if (!state.token) return;
        const hash = window.location.hash.replace(/^#\/?/, "");
        const path = hash.split("?", 1)[0];
        const [moduleId, ...rest] = path.split("/").filter(Boolean);
        const target = findModule(moduleId) ? moduleId : (readStorage(STORAGE.module) || "overview");
        const params = {};
        if (rest.length) params.id = decodeURIComponent(rest[0]);
        const query = hash.includes("?") ? hash.slice(hash.indexOf("?") + 1) : "";
        for (const [key, value] of new URLSearchParams(query)) params[key] = value;
        activateModule(target, params);
      }

      function navigate(moduleId, params = {}) {
        const module = findModule(moduleId);
        if (!module) return;
        let hash = `#/${moduleId}`;
        if (params.id !== undefined && params.id !== null) hash += `/${encodeURIComponent(params.id)}`;
        const query = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
          if (key === "id" || value === undefined || value === null || value === "") continue;
          query.set(key, String(value));
        }
        const queryString = query.toString();
        if (queryString) hash += `?${queryString}`;
        if (window.location.hash === hash) {
          activateModule(moduleId, params);
        } else {
          window.location.hash = hash;
        }
      }

      async function activateModule(moduleId, params = {}, options = {}) {
        const module = findModule(moduleId) || modules[0];
        if (!module) return;
        const previous = state.activeModule;
        if (previous && previous !== module.id) {
          const previousModule = findModule(previous);
          previousModule?.unload?.();
        }
        stopPolling();
        state.activeModule = module.id;
        state.moduleParams = params;
        writeStorage(STORAGE.module, module.id);
        renderNav();
        els.moduleKicker.textContent = t(`nav.group.${moduleGroups.find(group => group.modules.includes(module.id))?.id || "overview"}`);
        els.moduleTitle.textContent = t(`nav.${module.id}`);
        els.moduleActions.innerHTML = "";
        if (!options.keepCache || options.force) {
          els.moduleRoot.innerHTML = String(loadingState());
        }

        const token = Symbol("render");
        state.renderToken = token;
        try {
          if (module.load) await module.load({ force: Boolean(options.force), params });
          if (state.renderToken !== token) return;
          module.render(els.moduleRoot, params);
          startPolling(module);
        } catch (error) {
          if (state.renderToken !== token) return;
          els.moduleRoot.innerHTML = String(html`<div class="panel"><div class="empty bad">${errorMessage(error)}</div></div>`);
        }
      }

      function startPolling(module) {
        stopPolling();
        if (!module || !module.poll || !module.tick) return;
        state.pollTimer = setInterval(() => {
          if (document.hidden || state.activeModule !== module.id) return;
          module.tick().catch(() => {});
        }, module.poll);
      }

      function stopPolling() {
        if (state.pollTimer) clearInterval(state.pollTimer);
        state.pollTimer = null;
      }

      function setModuleActions(content) {
        els.moduleActions.innerHTML = String(content || "");
      }

      // Re-render the active module without reloading data.
      function rerender() {
        const module = findModule(state.activeModule);
        if (module) module.render(els.moduleRoot, state.moduleParams);
      }

""";
    }
}
