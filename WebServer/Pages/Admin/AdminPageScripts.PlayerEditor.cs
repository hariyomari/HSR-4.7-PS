namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string PlayerEditorModule()
    {
        return """
      // ======================================================== player editor
      // Edits one save, section by section. Every change runs offline on the server (an online player is
      // disconnected first) and keeps the save from before it as a one-step undo point.
      const editorState = {
        uid: null,
        query: "",
        tab: "profile",
        summary: null,
        summaryError: "",
        catalog: null,
        sections: {},
        loading: {},
        errors: {},
        rawTable: "",
        inventoryQuery: "",
        inventoryPage: { materials: 1, light_cones: 1, relics: 1 },
        selectedGear: new Set(),
        log: []
      };
      const EDITOR_TABS = ["profile", "characters", "inventory", "teams", "story", "raw"];
      const EDITOR_SECTION = { profile: "profile", characters: "avatars", inventory: "inventory", teams: "lineups", story: "story", raw: "raw" };

      registerModule({
        id: "editor",
        icon: ICONS.editor,
        async load({ force, params }) {
          if (!accountsState.loaded || force) await loadAccounts();
          const uid = params.id ? Number(params.id) : editorState.uid;
          if (params.tab && EDITOR_TABS.includes(params.tab)) editorState.tab = params.tab;
          if (uid !== editorState.uid || force) resetEditor(uid);
          if (!editorState.catalog) editorState.catalog = await api("/admin/api/editor/catalog");
          await handbook.ensureDefault();
          if (editorState.uid) await loadEditorSummary();
        },
        render(root) {
          renderEditorPage(root);
        }
      });

      function resetEditor(uid) {
        editorState.uid = uid || null;
        editorState.summary = null;
        editorState.summaryError = "";
        editorState.sections = {};
        editorState.loading = {};
        editorState.errors = {};
        editorState.rawTable = "";
        editorState.selectedGear = new Set();
        editorState.log = [];
      }

      function editorBase(uid = editorState.uid) {
        return `/admin/api/players/${uid}/editor`;
      }

      function editorAccount() {
        return accountsState.list.find(account => account.uid === editorState.uid) || null;
      }

      async function loadEditorSummary() {
        try {
          editorState.summary = await api(editorBase());
          editorState.summaryError = "";
        } catch (error) {
          editorState.summary = null;
          editorState.summaryError = errorMessage(error);
        }
      }

      // Loads one section once; render functions call it and show a loading state meanwhile.
      function ensureEditorSection(section, path) {
        const uid = editorState.uid;
        if (editorState.sections[section] || editorState.loading[section] || editorState.errors[section]) return;
        editorState.loading[section] = true;
        api(path || `${editorBase(uid)}/${section}`).then(data => {
          if (editorState.uid !== uid) return;
          editorState.sections[section] = data;
        }).catch(error => {
          if (editorState.uid !== uid) return;
          editorState.errors[section] = errorMessage(error);
        }).finally(() => {
          if (editorState.uid !== uid) return;
          editorState.loading[section] = false;
          renderEditorPanel();
        });
      }

      function sectionState(section, path) {
        if (editorState.errors[section]) return { error: editorState.errors[section] };
        const data = editorState.sections[section];
        if (!data) {
          ensureEditorSection(section, path);
          return { loading: true };
        }
        return { data };
      }

      // After a change every section may differ (a removed character leaves the teams, a mission action moves the
      // player), so all of them reload on demand.
      async function reloadEditor() {
        editorState.sections = {};
        editorState.loading = {};
        editorState.errors = {};
        await loadEditorSummary();
        await refreshAccounts();
        renderEditorAccounts();
        renderEditorPanel();
      }

      // Runs one change. A refused change (stale version, busy save) reloads too, so the form shows the save as it is.
      async function editorChange(button, path, options, successMessage) {
        let failed = false;
        const data = await run(button, async () => {
          try {
            return await api(path, options);
          } catch (error) {
            failed = true;
            throw error;
          }
        }, successMessage);
        if (data?.was_online) toast(t("editor.kicked"), "warn");
        if (data || failed) await reloadEditor();
        return data;
      }

      function renderEditorPage(root) {
        root.innerHTML = String(html`
          <div class="split editor-split">
            <section class="panel">
              <div class="toolbar">
                <input id="ed-search" type="search" placeholder="${t("accounts.search_placeholder")}" value="${editorState.query}">
              </div>
              <div id="ed-accounts"></div>
            </section>
            <section class="panel" id="ed-panel"></section>
          </div>`);
        renderEditorAccounts();
        renderEditorPanel();
        $("#ed-search").addEventListener("input", event => {
          editorState.query = event.currentTarget.value;
          renderEditorAccounts();
        });
      }

      function renderEditorAccounts() {
        const host = $("#ed-accounts");
        if (!host) return;
        const query = editorState.query.trim().toLowerCase();
        const rows = accountsState.list
          .filter(account => !query || [account.uid, account.username, account.nickname].join(" ").toLowerCase().includes(query))
          .sort((a, b) => a.uid - b.uid)
          .slice(0, 200);
        host.innerHTML = String(rows.length ? html`
          <div class="table-wrap">
            <table aria-label="${t("nav.accounts")}">
              <thead><tr><th class="w-uid">UID</th><th>${t("common.user")}</th><th class="w-sm">${t("common.level")}</th></tr></thead>
              <tbody>${rows.map(account => html`
                <tr class="clickable ${account.uid === editorState.uid ? "selected" : ""}" data-ed-uid="${account.uid}">
                  <td class="mono">${account.uid}</td>
                  <td><strong>${account.username}</strong>
                    <div class="muted small">${account.nickname || t("common.no_nickname")}
                      ${account.online ? chip(t("common.online"), "good") : ""}${account.delete_on_logout ? chip(t("editor.test_account"), "warn") : ""}</div></td>
                  <td>${account.level ?? "-"}</td>
                </tr>`)}</tbody>
            </table>
          </div>` : emptyState(t("accounts.empty")));
        on(host, "[data-ed-uid]", "click", event => navigate("editor", { id: Number(event.currentTarget.dataset.edUid), tab: editorState.tab }));
      }

      function renderEditorPanel() {
        const host = $("#ed-panel");
        if (!host) return;
        if (!editorState.uid) {
          host.innerHTML = String(html`<div class="panel-head"><h3>${t("nav.editor")}</h3></div>${emptyState(t("editor.select_hint"))}`);
          return;
        }
        const account = editorAccount();
        const summary = editorState.summary;
        if (!summary) {
          host.innerHTML = String(editorState.summaryError ? html`<div class="empty bad">${editorState.summaryError}</div>` : loadingState());
          return;
        }

        host.innerHTML = String(html`
          <div class="panel-head">
            <div>
              <h3>${account?.username || `UID ${summary.uid}`}</h3>
              <div class="muted small">UID ${summary.uid}${account?.nickname ? html` &middot; ${account.nickname}` : ""}</div>
            </div>
            <span class="chip-list">
              ${chip(summary.online ? t("common.online") : t("common.offline"), summary.online ? "good" : "subtle")}
              ${summary.busy ? chip(t("editor.busy"), "warn") : ""}
              ${summary.delete_on_logout ? chip(t("editor.test_account"), "warn") : ""}
            </span>
          </div>
          <div class="panel-body stack">
            ${summary.online ? html`<div class="notice warn">${t("editor.online_notice")}</div>` : ""}
            <div class="row space-between">
              <span class="muted small">${summary.undo ? t("editor.undo_point", { label: summary.undo.label, time: fmt.unix(summary.undo.created_at) }) : t("editor.no_undo")}</span>
              <div class="row">
                <button class="btn ghost small" type="button" id="ed-undo" ${summary.undo ? "" : "disabled"}>${t("editor.undo")}</button>
                <button class="btn ghost small" type="button" id="ed-refresh">${t("common.refresh")}</button>
                <button class="btn ghost small" type="button" id="ed-open-account">${t("editor.open_account")}</button>
              </div>
            </div>
          </div>
          ${summary.exists ? html`
            <div class="tabs" role="tablist">
              ${EDITOR_TABS.map(tab => html`<button class="tab ${editorState.tab === tab ? "active" : ""}" type="button" role="tab" data-ed-tab="${tab}">${t(`editor.tab.${tab}`)}</button>`)}
            </div>
            <div class="panel-body">${renderEditorTab()}</div>` : html`
            <div class="panel-body stack">
              ${emptyState(t("accounts.player_missing"))}
              <div class="actions"><button class="btn primary" type="button" data-ed-story="reset">${t("story.jump")}</button></div>
            </div>`}`);

        $("#ed-undo", host).addEventListener("click", async event => {
          if (!await confirmDialog({ title: t("editor.undo"), message: t("editor.undo_confirm", { label: summary.undo.label }), confirmLabel: t("editor.undo") })) return;
          await editorChange(event.currentTarget, `${editorBase()}/undo`, { method: "POST" }, t("editor.undone"));
        });
        $("#ed-refresh", host).addEventListener("click", () => reloadEditor());
        $("#ed-open-account", host).addEventListener("click", () => navigate("accounts", { id: editorState.uid, tab: "player" }));
        on(host, "[data-ed-tab]", "click", event => {
          editorState.tab = event.currentTarget.dataset.edTab;
          renderEditorPanel();
        });
        on(host, "[data-ed-story]", "click", event => {
          if (!account) return;
          openStoryDialog(account, { keepSave: event.currentTarget.dataset.edStory === "advance", onDone: () => reloadEditor() });
        });
        bindEditorTab(host);
      }

      function renderEditorTab() {
        switch (editorState.tab) {
          case "characters": return renderEditorCharacters();
          case "inventory": return renderEditorInventory();
          case "teams": return renderEditorTeams();
          case "story": return renderEditorStory();
          case "raw": return renderEditorRaw();
          default: return renderEditorProfile();
        }
      }

      function bindEditorTab(host) {
        switch (editorState.tab) {
          case "characters": return bindEditorCharacters(host);
          case "inventory": return bindEditorInventory(host);
          case "teams": return bindEditorTeams(host);
          case "story": return bindEditorStory(host);
          case "raw": return bindEditorRaw(host);
          default: return bindEditorProfile(host);
        }
      }

      function sectionFallback(state) {
        return state.error ? html`<div class="empty bad">${state.error}</div>` : loadingState();
      }

      function numberField(id, label, value, attrs = "") {
        return html`<label class="field"><span>${label}</span><input id="${id}" type="number" value="${value}" ${raw(attrs)}></label>`;
      }

      // ------------------------------------------------------------- profile
      function renderEditorProfile() {
        const state = sectionState("profile");
        if (!state.data) return sectionFallback(state);
        const profile = state.data.profile;
        const summary = editorState.summary;
        return html`
          <form id="ed-profile-form" class="form-grid">
            <label class="field"><span>${t("common.nickname")}</span><input id="ed-name" value="${profile.name}" maxlength="32" required></label>
            <label class="field"><span>${t("player.head_icon")}</span><input id="ed-head-icon" type="number" min="1" value="${profile.head_icon}"></label>
            <label class="field wide"><span>${t("player.signature")}</span><input id="ed-signature" value="${profile.signature}" maxlength="120"></label>
            ${numberField("ed-level", t("accounts.trailblaze_level"), profile.level, `min="1" max="${summary.max_player_level}"`)}
            ${numberField("ed-exp", t("common.exp"), profile.exp, `min="0"`)}
            ${numberField("ed-world-level", t("sessions.world_level"), profile.world_level, `min="0" max="${summary.max_world_level}"`)}
            ${numberField("ed-scoin", t("player.credits"), profile.scoin, `min="0"`)}
            ${numberField("ed-hcoin", t("player.jade"), profile.hcoin, `min="0"`)}
            ${numberField("ed-mcoin", t("player.shards"), profile.mcoin, `min="0"`)}
            ${numberField("ed-stamina", t("player.stamina"), profile.stamina, `min="0"`)}
            ${numberField("ed-reserve", t("player.reserve"), profile.stamina_reserve, `min="0" step="0.1"`)}
            <label class="field"><span>${t("player.month_card")}</span><input id="ed-month-card" type="datetime-local" value="${profile.month_card_expire > 0 ? unixToDateTimeLocal(profile.month_card_expire) : ""}"></label>
            ${profile.gender_set
              ? html`<label class="field"><span>${t("story.gender")}</span><select id="ed-gender">${option("woman", t("story.gender.woman"), profile.gender === "Man" ? "man" : "woman")}${option("man", t("story.gender.man"), profile.gender === "Man" ? "man" : "woman")}</select></label>`
              : html`<div class="field">${kv(t("story.gender"), t("story.report.gender_unset"))}</div>`}
            <p class="wide muted small">${t("editor.profile_hint")}</p>
            <div class="actions wide"><button class="btn primary" type="submit">${t("editor.save")}</button></div>
          </form>`;
      }

      function bindEditorProfile(host) {
        const form = $("#ed-profile-form", host);
        if (!form) return;
        form.addEventListener("submit", async event => {
          event.preventDefault();
          const value = id => $(id, form).value;
          const monthCard = value("#ed-month-card");
          await editorChange($("button[type=submit]", form), `${editorBase()}/profile`, {
            method: "PUT",
            body: {
              name: value("#ed-name").trim(),
              signature: value("#ed-signature"),
              head_icon: Number(value("#ed-head-icon")),
              level: Number(value("#ed-level")),
              exp: Number(value("#ed-exp")),
              world_level: Number(value("#ed-world-level")),
              scoin: Number(value("#ed-scoin")),
              hcoin: Number(value("#ed-hcoin")),
              mcoin: Number(value("#ed-mcoin")),
              stamina: Number(value("#ed-stamina")),
              stamina_reserve: Number(value("#ed-reserve")),
              month_card_expire: monthCard ? dateTimeLocalToUnix(monthCard) : 0,
              gender: $("#ed-gender", form)?.value || "keep",
              version: editorState.sections.profile.version
            }
          }, t("editor.saved"));
        });
      }

      // ---------------------------------------------------------- characters
      function avatarLabel(id) {
        return handbook.label("Avatar", id);
      }

      function renderEditorCharacters() {
        const state = sectionState("avatars");
        if (!state.data) return sectionFallback(state);
        const avatars = state.data.avatars;
        const owned = new Set(avatars.flatMap(avatar => avatar.paths));
        const addable = editorState.catalog.avatars.filter(avatar => !owned.has(avatar.id));
        return html`
          <div class="stack">
            <form id="ed-avatar-add" class="input-row">
              <input id="ed-avatar-id" list="ed-avatar-list" placeholder="${t("editor.avatar_pick")}" autocomplete="off">
              <datalist id="ed-avatar-list">${addable.map(avatar => html`<option value="${avatar.id}">${avatarLabel(avatar.id)}${avatar.rarity ? ` · ${avatar.rarity}★` : ""}</option>`)}</datalist>
              <button class="btn primary" type="submit">${t("editor.avatar_add")}</button>
            </form>
            <div class="table-wrap">
              <table>
                <thead><tr><th class="w-uid">ID</th><th>${t("common.name")}</th><th class="w-sm">${t("common.level")}</th><th class="w-sm">${t("player.promotion")}</th><th class="w-sm">${t("player.eidolon")}</th><th>${t("editor.light_cone")}</th><th class="w-sm">${t("editor.relics")}</th><th class="w-sm">${t("editor.traces")}</th><th></th></tr></thead>
                <tbody>${avatars.map(avatar => html`
                  <tr>
                    <td class="mono">${avatar.id}${avatar.paths.length > 1 ? html`<div class="muted small">${avatar.paths.join(", ")}</div>` : ""}</td>
                    <td>${handbook.name("Avatar", avatar.avatar_id) || handbook.name("Avatar", avatar.id) || html`<span class="muted">-</span>`}</td>
                    <td>${avatar.level}</td>
                    <td>${avatar.promotion}</td>
                    <td>${avatar.rank}</td>
                    <td>${avatar.light_cone_id ? handbook.label("Item", avatar.light_cone_id) : html`<span class="muted">-</span>`}</td>
                    <td>${avatar.relic_count}</td>
                    <td>${avatar.traces_maxed ? chip(t("editor.max"), "good") : ""}</td>
                    <td><div class="row">
                      <button class="btn ghost small" type="button" data-ed-avatar-edit="${avatar.id}">${t("editor.edit")}</button>
                      <button class="btn ghost small" type="button" data-ed-avatar-gear="${avatar.id}">${t("editor.gear")}</button>
                      ${avatar.id === 8001 ? "" : html`<button class="btn danger small" type="button" data-ed-avatar-remove="${avatar.id}">${t("common.remove")}</button>`}
                    </div></td>
                  </tr>`)}</tbody>
              </table>
            </div>
          </div>`;
      }

      function promotionOptions() {
        return [["-1", t("editor.promotion_auto")], ...[0, 1, 2, 3, 4, 5, 6].map(value => [String(value), String(value)])];
      }

      function bindEditorCharacters(host) {
        const addForm = $("#ed-avatar-add", host);
        if (!addForm) return;
        addForm.addEventListener("submit", async event => {
          event.preventDefault();
          const id = Number($("#ed-avatar-id", addForm).value);
          if (!id) return;
          await editorChange($("button[type=submit]", addForm), `${editorBase()}/avatars`, { method: "POST", body: { avatar_id: id } }, t("editor.avatar_added", { name: avatarLabel(id) }));
        });
        on(host, "[data-ed-avatar-edit]", "click", async event => {
          const id = Number(event.currentTarget.dataset.edAvatarEdit);
          const avatar = editorState.sections.avatars.avatars.find(item => item.id === id);
          const values = await formDialog({
            title: t("editor.avatar_edit_title", { name: avatarLabel(id) }),
            fields: [
              { name: "level", label: t("common.level"), type: "number", value: avatar.level, required: true },
              { name: "promotion", label: t("player.promotion"), type: "select", value: "-1", options: promotionOptions() },
              { name: "rank", label: t("player.eidolon"), type: "number", value: avatar.rank, required: true },
              { name: "max_traces", label: t("editor.traces"), type: "select", value: "", options: [["", t("editor.traces_keep")], ["1", t("editor.traces_max")]] }
            ],
            confirmLabel: t("editor.save")
          });
          if (!values) return;
          await editorChange(event.currentTarget, `${editorBase()}/avatars/${id}`, {
            method: "PUT",
            body: { level: Number(values.level), promotion: Number(values.promotion), rank: Number(values.rank), max_traces: values.max_traces === "1", version: editorState.sections.avatars.version }
          }, t("editor.saved"));
        });
        on(host, "[data-ed-avatar-remove]", "click", async event => {
          const id = Number(event.currentTarget.dataset.edAvatarRemove);
          if (!await confirmDialog({ title: t("editor.avatar_remove_title", { name: avatarLabel(id) }), message: t("editor.avatar_remove_message"), danger: true, confirmLabel: t("common.remove") })) return;
          await editorChange(event.currentTarget, `${editorBase()}/avatars/${id}`, { method: "DELETE" }, t("editor.saved"));
        });
        on(host, "[data-ed-avatar-gear]", "click", event => openGearDialog(Number(event.currentTarget.dataset.edAvatarGear)));
      }

      // Light cone and relics of a character's current path. Only changed slots are sent.
      async function openGearDialog(id) {
        const avatar = editorState.sections.avatars.avatars.find(item => item.id === id);
        let inventory = editorState.sections.inventory;
        if (!inventory) {
          inventory = await run(null, () => api(`${editorBase()}/inventory`));
          if (!inventory) return;
          editorState.sections.inventory = inventory;
        }
        const worn = Object.fromEntries(avatar.relics.map(relic => [relic.slot, relic.unique_id]));
        const wearer = item => item.equip_avatar ? ` · ${avatarLabel(item.equip_avatar)}` : "";
        const modal = accountModal(html`
          <h3>${t("editor.gear_title", { name: avatarLabel(avatar.avatar_id || id) })}</h3>
          <form id="ed-gear-form" class="form-grid">
            <label class="field wide"><span>${t("editor.light_cone")}</span>
              <select name="light_cone">${option(0, t("editor.empty_slot"), avatar.light_cone_unique_id)}${inventory.light_cones.map(item => option(item.unique_id, `${itemLabel(item.item_id)} · Lv ${item.level} · S${item.rank}${wearer(item)}`, avatar.light_cone_unique_id))}</select>
            </label>
            ${[1, 2, 3, 4, 5, 6].map(slot => html`
              <label class="field"><span>${t(`editor.slot.${slot}`)}</span>
                <select name="slot${slot}">${option(0, t("editor.empty_slot"), worn[slot] || 0)}${inventory.relics.filter(item => item.slot === slot).map(item => option(item.unique_id, `${itemLabel(item.item_id)} · +${item.level}${wearer(item)}`, worn[slot] || 0))}</select>
              </label>`)}
            <div class="modal-actions wide">
              <button class="btn ghost" type="button" data-gear-cancel>${t("common.cancel")}</button>
              <button class="btn primary" type="submit">${t("editor.save")}</button>
            </div>
          </form>`);
        const form = $("#ed-gear-form", modal.element);
        $("[data-gear-cancel]", form).addEventListener("click", () => modal.close());
        form.addEventListener("submit", async event => {
          event.preventDefault();
          const values = Object.fromEntries(new FormData(form).entries());
          const body = { relics: {} };
          if (Number(values.light_cone) !== avatar.light_cone_unique_id) body.light_cone_unique_id = Number(values.light_cone);
          for (const slot of [1, 2, 3, 4, 5, 6]) {
            const value = Number(values[`slot${slot}`]);
            if (value !== (worn[slot] || 0)) body.relics[slot] = value;
          }
          const data = await editorChange($("button[type=submit]", form), `${editorBase()}/avatars/${id}/gear`, { method: "PUT", body }, t("editor.saved"));
          if (data) modal.close();
        });
      }

      // ----------------------------------------------------------- inventory
      function itemLabel(id) {
        return handbook.label("Item", id);
      }

      function renderEditorInventory() {
        const state = sectionState("inventory");
        if (!state.data) return sectionFallback(state);
        const data = state.data;
        const catalog = editorState.catalog;
        const query = editorState.inventoryQuery.trim().toLowerCase();
        const matches = id => !query || `${id} ${handbook.name("Item", id)}`.toLowerCase().includes(query);
        const materials = paginate(data.materials.filter(item => matches(item.item_id)), editorState.inventoryPage.materials, 25);
        const lightCones = paginate(data.light_cones.filter(item => matches(item.item_id)), editorState.inventoryPage.light_cones, 25);
        const relics = paginate(data.relics.filter(item => matches(item.item_id)), editorState.inventoryPage.relics, 25);
        const equipped = id => id ? html`${avatarLabel(id)}` : html`<span class="muted">-</span>`;
        const check = item => html`<input type="checkbox" data-ed-gear="${item.unique_id}" ${editorState.selectedGear.has(item.unique_id) ? "checked" : ""}>`;
        return html`
          <div class="stack">
            <div class="section-title">${t("editor.grant")}</div>
            <form id="ed-grant" class="form-grid">
              <label class="field wide"><span>${t("editor.item")}</span>
                <input id="ed-grant-item" list="ed-item-list" placeholder="${t("editor.item_pick")}" autocomplete="off" required>
                <datalist id="ed-item-list">${[...catalog.light_cones, ...catalog.relics, ...catalog.items].map(id => html`<option value="${id}">${itemLabel(id)}</option>`)}</datalist>
              </label>
              ${numberField("ed-grant-count", t("editor.count"), 1, `min="1"`)}
              ${numberField("ed-grant-level", t("common.level"), 1, `min="0" max="80"`)}
              ${numberField("ed-grant-rank", t("editor.superimpose"), 1, `min="1" max="5"`)}
              <p class="wide muted small">${t("editor.grant_hint")}</p>
              <div class="actions wide"><button class="btn primary" type="submit">${t("editor.grant_button")}</button></div>
            </form>
            <div class="row space-between">
              <input id="ed-inv-search" type="search" placeholder="${t("editor.item_filter")}" value="${editorState.inventoryQuery}">
              <button class="btn danger small" type="button" id="ed-gear-remove" ${editorState.selectedGear.size ? "" : "disabled"}>${t("editor.remove_selected", { count: editorState.selectedGear.size })}</button>
            </div>
            <div class="section-title">${t("editor.materials", { count: data.materials.length })}</div>
            ${materials.items.length ? html`
              <div class="table-wrap short"><table>
                <thead><tr><th class="w-uid">ID</th><th>${t("common.name")}</th><th class="w-md">${t("editor.count")}</th><th></th></tr></thead>
                <tbody>${materials.items.map(item => html`<tr>
                  <td class="mono">${item.item_id}</td><td>${handbook.name("Item", item.item_id) || html`<span class="muted">-</span>`}</td>
                  <td><input type="number" min="0" value="${item.count}" data-ed-count-input="${item.item_id}"></td>
                  <td><button class="btn ghost small" type="button" data-ed-count="${item.item_id}">${t("editor.save")}</button></td>
                </tr>`)}</tbody>
              </table></div>${pager(materials, "ed-page-materials")}` : emptyState(t("common.none"))}
            <div class="section-title">${t("editor.light_cones", { count: data.light_cones.length, max: data.max_light_cones })}</div>
            ${lightCones.items.length ? html`
              <div class="table-wrap short"><table>
                <thead><tr><th></th><th class="w-uid">UID</th><th>${t("editor.item")}</th><th class="w-sm">${t("common.level")}</th><th class="w-sm">${t("player.promotion")}</th><th class="w-sm">${t("editor.superimpose")}</th><th>${t("editor.equipped")}</th><th></th></tr></thead>
                <tbody>${lightCones.items.map(item => html`<tr>
                  <td>${check(item)}</td><td class="mono">${item.unique_id}</td><td>${itemLabel(item.item_id)}</td>
                  <td>${item.level}</td><td>${item.promotion}</td><td>${item.rank}</td><td>${equipped(item.equip_avatar)}</td>
                  <td><button class="btn ghost small" type="button" data-ed-cone="${item.unique_id}">${t("editor.edit")}</button></td>
                </tr>`)}</tbody>
              </table></div>${pager(lightCones, "ed-page-cones")}` : emptyState(t("common.none"))}
            <div class="section-title">${t("editor.relic_list", { count: data.relics.length, max: data.max_relics })}</div>
            ${relics.items.length ? html`
              <div class="table-wrap short"><table>
                <thead><tr><th></th><th class="w-uid">UID</th><th>${t("editor.item")}</th><th class="w-sm">${t("common.level")}</th><th class="w-sm">${t("editor.main_affix")}</th><th class="w-sm">${t("editor.sub_affixes")}</th><th>${t("editor.equipped")}</th></tr></thead>
                <tbody>${relics.items.map(item => html`<tr>
                  <td>${check(item)}</td><td class="mono">${item.unique_id}</td><td>${itemLabel(item.item_id)}</td>
                  <td>${item.level}</td><td class="mono">${item.main_affix}</td><td>${item.sub_affixes}</td><td>${equipped(item.equip_avatar)}</td>
                </tr>`)}</tbody>
              </table></div>${pager(relics, "ed-page-relics")}` : emptyState(t("common.none"))}
          </div>`;
      }

      function bindEditorInventory(host) {
        const grant = $("#ed-grant", host);
        if (!grant) return;
        grant.addEventListener("submit", async event => {
          event.preventDefault();
          const itemId = Number($("#ed-grant-item", grant).value);
          if (!itemId) return;
          await editorChange($("button[type=submit]", grant), `${editorBase()}/inventory/grant`, {
            method: "POST",
            body: { item_id: itemId, count: Number($("#ed-grant-count", grant).value), level: Number($("#ed-grant-level", grant).value), rank: Number($("#ed-grant-rank", grant).value) }
          }, data => t("editor.granted", { count: data.count, item: itemLabel(itemId) }));
        });
        $("#ed-inv-search", host).addEventListener("input", debounce(event => {
          editorState.inventoryQuery = event.target.value;
          editorState.inventoryPage = { materials: 1, light_cones: 1, relics: 1 };
          renderEditorPanel();
          const input = $("#ed-inv-search");
          if (input) {
            input.focus();
            input.setSelectionRange(input.value.length, input.value.length);
          }
        }, 250));
        for (const [attr, key] of [["ed-page-materials", "materials"], ["ed-page-cones", "light_cones"], ["ed-page-relics", "relics"]]) {
          on(host, `[data-${attr}]`, "click", event => {
            editorState.inventoryPage[key] = Number(event.currentTarget.dataset[attr.replace(/-([a-z])/g, (_, c) => c.toUpperCase())]);
            renderEditorPanel();
          });
        }
        on(host, "[data-ed-gear]", "change", event => {
          const id = Number(event.currentTarget.dataset.edGear);
          if (event.currentTarget.checked) editorState.selectedGear.add(id);
          else editorState.selectedGear.delete(id);
          const button = $("#ed-gear-remove", host);
          button.disabled = !editorState.selectedGear.size;
          button.textContent = t("editor.remove_selected", { count: editorState.selectedGear.size });
        });
        $("#ed-gear-remove", host).addEventListener("click", async event => {
          const ids = [...editorState.selectedGear];
          if (!await confirmDialog({ title: t("editor.remove_selected", { count: ids.length }), message: t("editor.remove_gear_message"), danger: true, confirmLabel: t("common.remove") })) return;
          const data = await editorChange(event.currentTarget, `${editorBase()}/inventory/remove`, { method: "POST", body: { unique_ids: ids } }, result => t("editor.removed", { count: result.count }));
          if (data) editorState.selectedGear = new Set();
        });
        on(host, "[data-ed-count]", "click", async event => {
          const id = Number(event.currentTarget.dataset.edCount);
          const count = Number($(`[data-ed-count-input="${id}"]`, host).value);
          await editorChange(event.currentTarget, `${editorBase()}/inventory/items/${id}`, { method: "PUT", body: { count } }, t("editor.saved"));
        });
        on(host, "[data-ed-cone]", "click", async event => {
          const uniqueId = Number(event.currentTarget.dataset.edCone);
          const item = editorState.sections.inventory.light_cones.find(entry => entry.unique_id === uniqueId);
          const values = await formDialog({
            title: t("editor.cone_edit_title", { name: itemLabel(item.item_id) }),
            fields: [
              { name: "level", label: t("common.level"), type: "number", value: item.level, required: true },
              { name: "promotion", label: t("player.promotion"), type: "select", value: "-1", options: promotionOptions() },
              { name: "rank", label: t("editor.superimpose"), type: "number", value: item.rank, required: true }
            ],
            confirmLabel: t("editor.save")
          });
          if (!values) return;
          await editorChange(event.currentTarget, `${editorBase()}/inventory/light-cones/${uniqueId}`, {
            method: "PUT", body: { level: Number(values.level), promotion: Number(values.promotion), rank: Number(values.rank) }
          }, t("editor.saved"));
        });
      }

      // --------------------------------------------------------------- teams
      function renderEditorTeams() {
        const lineups = sectionState("lineups");
        const avatars = sectionState("avatars");
        if (!lineups.data) return sectionFallback(lineups);
        if (!avatars.data) return sectionFallback(avatars);
        const owned = avatars.data.avatars;
        const slotValue = slot => !slot ? "" : slot.special_avatar_id ? `t${slot.special_avatar_id}` : String(slot.base_avatar_id);
        const slotOptions = (slot, trials) => html`
          ${option("", t("editor.empty_slot"), slotValue(slot))}
          ${trials.map(id => option(`t${id}`, `${t("editor.trial")} ${id}`, slotValue(slot)))}
          ${owned.map(avatar => option(avatar.id, avatarLabel(avatar.avatar_id || avatar.id), slotValue(slot)))}`;
        return html`
          <div class="stack">
            ${lineups.data.extra_lineup >= 0 ? html`<div class="notice warn">${t("editor.extra_lineup", { index: lineups.data.extra_lineup })}</div>` : ""}
            <p class="muted small">${t("editor.teams_hint")}</p>
            ${lineups.data.lineups.map(lineup => {
              const trials = lineup.slots.filter(slot => slot.special_avatar_id > 0).map(slot => slot.special_avatar_id);
              return html`
                <form class="editor-team" data-ed-team="${lineup.index}">
                  <strong class="w-sm">${t("editor.team", { index: lineup.index + 1 })}</strong>
                  ${[0, 1, 2, 3].map(position => html`<select data-ed-slot="${position}">${slotOptions(lineup.slots[position], trials)}</select>`)}
                  <label class="row small"><input type="checkbox" data-ed-make-current ${lineup.current ? "checked disabled" : ""}> ${t("editor.current_team")}</label>
                  <button class="btn ghost small" type="submit">${t("editor.save")}</button>
                </form>`;
            })}
          </div>`;
      }

      function bindEditorTeams(host) {
        on(host, "[data-ed-team]", "submit", async event => {
          event.preventDefault();
          const form = event.currentTarget;
          const index = Number(form.dataset.edTeam);
          const slots = $$("[data-ed-slot]", form)
            .map(select => select.value)
            .filter(Boolean)
            .map(value => value.startsWith("t") ? { base_avatar_id: 0, special_avatar_id: Number(value.slice(1)) } : { base_avatar_id: Number(value), special_avatar_id: 0 });
          await editorChange($("button[type=submit]", form), `${editorBase()}/lineups/${index}`, {
            method: "PUT",
            body: { slots, make_current: $("[data-ed-make-current]", form).checked, version: editorState.sections.lineups.version }
          }, t("editor.saved"));
        });
      }

      // --------------------------------------------------------------- story
      function missionLabel(id) {
        return handbook.label("MainMission", id);
      }

      function renderEditorStory() {
        const state = sectionState("story");
        if (!state.data) return sectionFallback(state);
        const story = state.data;
        const entrances = editorState.catalog.entrances;
        return html`
          <div class="stack">
            <div class="kv-grid">
              ${kv(t("editor.mission_system"), story.mission_enabled ? chip(t("common.on"), "good") : chip(t("common.off"), "warn"))}
              ${kv(t("story.report.tracking"), story.tracking_main_mission_id ? missionLabel(story.tracking_main_mission_id) : t("common.none"))}
              ${kv(t("story.report.missions"), t("story.report.missions_value", { mains: story.finished_mains.length, subs: story.finished_subs }))}
              ${kv(t("story.report.spawn"), `${story.plane_id} / ${story.floor_id} / ${story.entry_id}`, { mono: true })}
              ${kv(t("story.report.teleports"), story.teleports_unlocked)}
              ${kv(t("editor.tutorials_done"), `${story.tutorials_finished} / ${story.tutorials_total}`)}
            </div>
            <div class="section-title">${t("editor.story_point")}</div>
            <p class="muted small">${t("editor.story_point_hint")}</p>
            <div class="actions">
              <button class="btn primary" type="button" data-ed-story="advance">${t("editor.story_advance")}</button>
              <button class="btn danger" type="button" data-ed-story="reset">${t("story.jump")}</button>
            </div>
            <div class="section-title">${t("editor.running_missions", { count: story.running.length })}</div>
            ${story.running.length ? html`
              <div class="table-wrap short"><table>
                <thead><tr><th>${t("story.main_mission")}</th><th>${t("editor.running_subs")}</th><th></th></tr></thead>
                <tbody>${story.running.map(mission => html`<tr>
                  <td>${missionLabel(mission.main_id)}${mission.main_id === story.tracking_main_mission_id ? html` ${chip(t("editor.tracked"), "accent")}` : ""}</td>
                  <td><span class="chip-list">${mission.sub_ids.map(id => html`<button type="button" class="chip clickable" data-ed-mission="finish_sub" data-ed-mission-id="${id}" title="${t("editor.finish_sub")}">${id} ✓</button>`)}</span></td>
                  <td><div class="row">
                    <button class="btn ghost small" type="button" data-ed-mission="finish_main" data-ed-mission-id="${mission.main_id}">${t("editor.finish_main")}</button>
                    <button class="btn ghost small" type="button" data-ed-mission="track" data-ed-mission-id="${mission.main_id}">${t("editor.track")}</button>
                    <button class="btn ghost small" type="button" data-ed-mission="reaccept_main" data-ed-mission-id="${mission.main_id}">${t("editor.reaccept")}</button>
                    <button class="btn danger small" type="button" data-ed-mission="remove_main" data-ed-mission-id="${mission.main_id}">${t("common.remove")}</button>
                  </div></td>
                </tr>`)}</tbody>
              </table></div>` : emptyState(t("common.none"))}
            <div class="section-title">${t("editor.mission_tools")}</div>
            <div class="row">
              <input id="ed-advance-count" type="number" min="1" max="10" value="1" class="w-sm">
              ${toggleSwitch("ed-advance-strict", false, t("editor.strict"))}
              <button class="btn ghost small" type="button" data-ed-mission="advance">${t("editor.advance")}</button>
              <button class="btn ghost small" type="button" data-ed-mission="rollback">${t("editor.rollback")}</button>
              <button class="btn ghost small" type="button" data-ed-mission="repair">${t("editor.repair")}</button>
              <button class="btn ghost small" type="button" data-ed-mission="${story.mission_enabled ? "disable" : "enable"}">${story.mission_enabled ? t("editor.missions_off") : t("editor.missions_on")}</button>
            </div>
            <div class="row">
              <input id="ed-mission-id" type="number" min="1" placeholder="${t("editor.mission_id")}">
              <button class="btn ghost small" type="button" data-ed-mission-input="accept_main">${t("editor.accept_main")}</button>
              <button class="btn ghost small" type="button" data-ed-mission-input="finish_main">${t("editor.finish_main")}</button>
              <button class="btn ghost small" type="button" data-ed-mission-input="finish_sub">${t("editor.finish_sub")}</button>
            </div>
            ${editorState.log.length ? html`<pre class="editor-log">${editorState.log.join("\n")}</pre>` : ""}
            <div class="section-title">${t("editor.world")}</div>
            <div class="row">
              <button class="btn ghost small" type="button" data-ed-teleports="reached_planes">${t("editor.teleports_reached")}</button>
              <button class="btn ghost small" type="button" data-ed-teleports="all">${t("editor.teleports_all")}</button>
              <button class="btn ghost small" type="button" id="ed-tutorials">${t("editor.tutorials_finish")}</button>
            </div>
            <form id="ed-position" class="input-row">
              <input id="ed-entry" list="ed-entry-list" placeholder="${t("editor.entrance_pick")}" value="${story.entry_id || ""}" autocomplete="off">
              <datalist id="ed-entry-list">${entrances.map(entrance => html`<option value="${entrance.id}">${entrance.plane_id} / ${entrance.floor_id} · ${entrance.plane_type}</option>`)}</datalist>
              <button class="btn ghost small" type="submit">${t("editor.move")}</button>
            </form>
            <details>
              <summary>${t("editor.finished_mains", { count: story.finished_mains.length })}</summary>
              <p class="mono small">${story.finished_mains.join(", ")}</p>
            </details>
          </div>`;
      }

      function bindEditorStory(host) {
        if (!$("#ed-position", host)) return;
        const runMission = async (button, action, missionId) => {
          const body = { action, mission_id: missionId || 0 };
          if (action === "advance") {
            body.count = Number($("#ed-advance-count", host).value) || 1;
            body.strict = $("#ed-advance-strict", host).checked;
          }
          if (action === "remove_main" && !await confirmDialog({ title: t("editor.remove_main_title", { id: missionId }), message: t("editor.remove_main_message"), danger: true, confirmLabel: t("common.remove") })) return;
          const data = await editorChange(button, `${editorBase()}/story/missions`, { method: "POST", body }, t("editor.done"));
          if (data) {
            editorState.log = data.log;
            renderEditorPanel();
          }
        };
        on(host, "[data-ed-mission]", "click", event => runMission(event.currentTarget, event.currentTarget.dataset.edMission, Number(event.currentTarget.dataset.edMissionId || 0)));
        on(host, "[data-ed-mission-input]", "click", event => {
          const id = Number($("#ed-mission-id", host).value);
          if (!id) {
            toast(t("editor.mission_id_missing"), "bad");
            return;
          }
          runMission(event.currentTarget, event.currentTarget.dataset.edMissionInput, id);
        });
        on(host, "[data-ed-teleports]", "click", async event => {
          await editorChange(event.currentTarget, `${editorBase()}/story/teleports`, { method: "POST", body: { mode: event.currentTarget.dataset.edTeleports } }, data => t("editor.teleports_done", { count: data.count }));
        });
        $("#ed-tutorials", host).addEventListener("click", async event => {
          await editorChange(event.currentTarget, `${editorBase()}/story/tutorials`, { method: "POST" }, data => t("editor.tutorials_done_count", { count: data.count }));
        });
        $("#ed-position", host).addEventListener("submit", async event => {
          event.preventDefault();
          const entryId = Number($("#ed-entry", host).value);
          if (!entryId) return;
          await editorChange($("button[type=submit]", event.currentTarget), `${editorBase()}/position`, { method: "PUT", body: { entry_id: entryId } }, t("editor.saved"));
        });
      }

      // ----------------------------------------------------------------- raw
      function renderEditorRaw() {
        const tables = sectionState("raw");
        if (!tables.data) return sectionFallback(tables);
        const present = tables.data.tables.filter(table => table.present);
        if (!editorState.rawTable || !present.some(table => table.table === editorState.rawTable))
          editorState.rawTable = (present.find(table => table.table === "Player") || present[0])?.table || "";
        const key = `raw:${editorState.rawTable}`;
        const table = editorState.rawTable ? sectionState(key, `${editorBase()}/raw/${encodeURIComponent(editorState.rawTable)}`) : { data: null };
        return html`
          <div class="stack">
            <div class="notice warn">${t("editor.raw_hint")}</div>
            <div class="row">
              <select id="ed-raw-table">${present.map(item => option(item.table, item.table, editorState.rawTable))}</select>
            </div>
            ${table.data ? html`
              ${table.data.editable ? "" : html`<div class="notice">${t("editor.raw_readonly")}</div>`}
              <textarea id="ed-raw-json" class="raw-json" spellcheck="false" ${table.data.editable ? "" : "readonly"}>${table.data.json}</textarea>
              <div class="actions"><button class="btn danger" type="button" id="ed-raw-save" ${table.data.editable ? "" : "disabled"}>${t("editor.raw_save")}</button></div>` : sectionFallback(table)}
          </div>`;
      }

      function bindEditorRaw(host) {
        const select = $("#ed-raw-table", host);
        if (!select) return;
        select.addEventListener("change", event => {
          editorState.rawTable = event.currentTarget.value;
          renderEditorPanel();
        });
        $("#ed-raw-save", host)?.addEventListener("click", async event => {
          const table = editorState.rawTable;
          if (!await confirmDialog({ title: t("editor.raw_save"), message: t("editor.raw_confirm", { table }), danger: true, requireText: "SAVE", confirmLabel: t("editor.raw_save") })) return;
          await editorChange(event.currentTarget, `${editorBase()}/raw/${encodeURIComponent(table)}`, {
            method: "PUT", body: { json: $("#ed-raw-json", host).value, version: editorState.sections[`raw:${table}`].version }
          }, t("editor.saved"));
        });
      }

""";
    }
}
