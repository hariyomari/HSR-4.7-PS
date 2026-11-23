namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string AccountsModule()
    {
        return """
      // ============================================================ accounts
      const accountsState = {
        loaded: false,
        list: [],
        roles: [],
        defaultRole: "User",
        query: "",
        filter: "all",
        sort: "uid",
        page: 1,
        pageSize: 25,
        checkedUids: new Set(),
        selectedUid: null,
        mode: "detail",
        tab: "profile",
        player: { uid: null, data: null, loading: false, error: "" },
        mailbox: { uid: null, data: null, loading: false, error: "" },
        storyCatalog: null
      };

      registerModule({
        id: "accounts",
        icon: ICONS.accounts,
        async load({ force, params }) {
          if (!accountsState.loaded || force) await loadAccounts();
          if (params.id) {
            accountsState.selectedUid = Number(params.id);
            accountsState.mode = "detail";
            if (params.tab) accountsState.tab = params.tab;
          }
        },
        render(root) {
          renderAccounts(root);
        }
      });

      async function loadAccounts() {
        const data = await api("/admin/api/accounts");
        accountsState.list = data.accounts || [];
        const existingUids = new Set(accountsState.list.map(account => account.uid));
        accountsState.checkedUids = new Set([...accountsState.checkedUids].filter(uid => existingUids.has(uid)));
        accountsState.roles = data.roles || [];
        accountsState.defaultRole = data.default_role || accountsState.roles[0] || "User";
        accountsState.loaded = true;
        if (accountsState.selectedUid && !accountsState.list.some(account => account.uid === accountsState.selectedUid)) {
          accountsState.selectedUid = null;
        }
      }

      async function refreshAccounts(selectedUid) {
        await loadAccounts();
        if (selectedUid !== undefined) accountsState.selectedUid = selectedUid;
        state.cache.overview = null;
        if (accountsState.player.uid === selectedUid) accountsState.player = { uid: null, data: null, loading: false, error: "" };
        renderAccountResults();
        renderAccountEditor();
      }

      function selectedAccount() {
        return accountsState.list.find(account => account.uid === accountsState.selectedUid) || null;
      }

      function filteredAccounts() {
        const query = accountsState.query.trim().toLowerCase();
        const filter = accountsState.filter;
        const rows = accountsState.list.filter(account => {
          if (filter === "online" && !account.online) return false;
          if (filter === "banned" && !account.banned) return false;
          if (filter === "unverified" && account.email_verified) return false;
          if (filter === "no-password" && account.has_password) return false;
          if (filter === "overrides" && !account.permissions) return false;
          if (filter.startsWith("role:") && account.role !== filter.slice(5)) return false;
          if (!query) return true;
          return [account.uid, account.username, account.email, account.role, account.nickname, account.permissions, account.ban_reason, account.last_ip, account.registration_ip, account.current_ip, ...(account.ip_addresses || []), ...(account.device_ids || [])]
            .join(" ").toLowerCase().includes(query);
        });

        const sorters = {
          uid: (a, b) => a.uid - b.uid,
          "uid-desc": (a, b) => b.uid - a.uid,
          username: (a, b) => String(a.username).localeCompare(String(b.username)),
          email: (a, b) => String(a.email).localeCompare(String(b.email)),
          level: (a, b) => (b.level ?? -1) - (a.level ?? -1) || a.uid - b.uid,
          online: (a, b) => Number(b.online) - Number(a.online) || a.uid - b.uid
        };
        return rows.sort(sorters[accountsState.sort] || sorters.uid);
      }

      function renderAccounts(root) {
        root.innerHTML = String(html`
          <div class="split">
            <section class="panel">
              <div class="toolbar">
                <input id="acc-search" type="search" placeholder="${t("accounts.search_placeholder")}" value="${accountsState.query}">
                <select id="acc-filter" aria-label="${t("common.filter")}">
                  ${option("all", t("accounts.filter.all"), accountsState.filter)}
                  ${option("online", t("accounts.filter.online"), accountsState.filter)}
                  ${option("banned", t("accounts.filter.banned"), accountsState.filter)}
                  ${option("unverified", t("accounts.filter.unverified"), accountsState.filter)}
                  ${option("no-password", t("accounts.filter.no_password"), accountsState.filter)}
                  ${option("overrides", t("accounts.filter.overrides"), accountsState.filter)}
                  ${accountsState.roles.map(role => option(`role:${role}`, t("accounts.filter.role", { role }), accountsState.filter))}
                </select>
                <select id="acc-sort" aria-label="${t("common.sort")}">
                  ${option("uid", t("accounts.sort.uid"), accountsState.sort)}
                  ${option("uid-desc", t("accounts.sort.uid_desc"), accountsState.sort)}
                  ${option("username", t("accounts.sort.username"), accountsState.sort)}
                  ${option("email", t("accounts.sort.email"), accountsState.sort)}
                  ${option("level", t("accounts.sort.level"), accountsState.sort)}
                  ${option("online", t("accounts.sort.online"), accountsState.sort)}
                </select>
                <button id="acc-new" class="btn primary" type="button">${t("accounts.new")}</button>
                <button id="acc-story" class="btn ghost" type="button">${t("story.create")}</button>
                <button id="acc-snapshots" class="btn ghost" type="button">${t("snapshots.open")}</button>
                <button id="acc-export" class="btn ghost" type="button" title="${t("accounts.export_hint")}">${t("accounts.export")}</button>
              </div>
              <div id="acc-results"></div>
            </section>
            <section class="panel editor" id="acc-editor"></section>
          </div>`);

        renderAccountResults();
        renderAccountEditor();

        // Typing must never rebuild the search box: replacing the live input drops focus and IME composition.
        $("#acc-search").addEventListener("input", event => {
          accountsState.checkedUids.clear();
          accountsState.query = event.currentTarget.value;
          accountsState.page = 1;
          renderAccountResults();
        });
        $("#acc-filter").addEventListener("change", event => {
          accountsState.checkedUids.clear();
          accountsState.filter = event.currentTarget.value;
          accountsState.page = 1;
          renderAccountResults();
        });
        $("#acc-sort").addEventListener("change", event => {
          accountsState.sort = event.currentTarget.value;
          renderAccountResults();
        });
        $("#acc-new").addEventListener("click", () => {
          accountsState.mode = "create";
          renderAccountResults();
          renderAccountEditor();
        });
        $("#acc-export").addEventListener("click", exportAccountsCsv);
        $("#acc-snapshots").addEventListener("click", () => openSnapshotsDialog());
        $("#acc-story").addEventListener("click", () => openStoryDialog(null));
      }

      function renderAccountResults() {
        const host = $("#acc-results");
        if (!host) return;
        const rows = filteredAccounts();
        const pagination = paginate(rows, accountsState.page, accountsState.pageSize);
        accountsState.page = pagination.page;

        host.innerHTML = String(html`
          <div class="toolbar">
            <button id="acc-select-filtered" class="btn ghost small" type="button" ${!rows.length || rows.length > 500 ? "disabled" : ""}>${t("accounts.select_filtered", { count: rows.length })}</button>
            <button id="acc-clear-selection" class="btn ghost small" type="button">${t("accounts.clear_selection")}</button>
            <button id="acc-bulk-delete" class="btn danger small" type="button" ${!accountsState.checkedUids.size || accountsState.checkedUids.size > 500 ? "disabled" : ""}>${t("accounts.bulk_delete", { count: accountsState.checkedUids.size })}</button>
          </div>
          <div class="table-meta">
            <span class="muted">${t("accounts.count", { shown: pagination.items.length, total: rows.length, all: accountsState.list.length })}</span>
            ${pager(pagination, "acc-page")}
          </div>
          ${pagination.items.length ? html`
            <div class="table-wrap">
              <table aria-label="${t("nav.accounts")}">
                <thead>
                  <tr>
                    <th><input id="acc-select-page" type="checkbox" aria-label="${t("accounts.select_page")}" ${pagination.items.every(a => accountsState.checkedUids.has(a.uid)) ? "checked" : ""}></th>
                    <th class="w-uid">UID</th>
                    <th>${t("common.user")}</th>
                    <th>${t("common.email")}</th>
                    <th>${t("accounts.last_ip")}</th>
                    <th class="w-md">${t("common.role")}</th>
                    <th class="w-sm">${t("common.level")}</th>
                    <th class="w-md">${t("common.state")}</th>
                  </tr>
                </thead>
                <tbody>
                  ${pagination.items.map(account => html`
                    <tr class="clickable ${account.uid === accountsState.selectedUid && accountsState.mode === "detail" ? "selected" : ""}" data-select-uid="${account.uid}">
                      <td><input type="checkbox" data-check-uid="${account.uid}" aria-label="${t("accounts.select_uid", { uid: account.uid })}" ${accountsState.checkedUids.has(account.uid) ? "checked" : ""}></td>
                      <td class="mono">${account.uid}</td>
                      <td><strong>${account.username}</strong><div class="muted small">${account.nickname || t("common.no_nickname")}</div></td>
                      <td class="muted">${account.email}</td>
                      <td class="mono">${account.current_ip || account.last_ip || t("blocks.no_records")}</td>
                      <td>${chip(account.role, account.permissions ? "accent" : "")}</td>
                      <td>${account.level ?? "-"}</td>
                      <td>${accountStateChips(account)}</td>
                    </tr>`)}
                </tbody>
              </table>
            </div>` : emptyState(t("accounts.empty"))}`);

        on(host, "[data-select-uid]", "click", event => {
          if (event.target.closest("input, button, a")) return;
          accountsState.selectedUid = Number(event.currentTarget.dataset.selectUid);
          accountsState.mode = "detail";
          for (const row of $$("[data-select-uid]", host)) row.classList.toggle("selected", row === event.currentTarget);
          renderAccountEditor();
          if (window.innerWidth < 1100) $("#acc-editor")?.scrollIntoView({ behavior: "smooth", block: "start" });
        });
        const selectPage = $("#acc-select-page", host);
        if (selectPage) {
          selectPage.addEventListener("change", event => {
            for (const account of pagination.items) {
              if (event.currentTarget.checked) accountsState.checkedUids.add(account.uid);
              else accountsState.checkedUids.delete(account.uid);
            }
            syncAccountSelection(host);
          });
        }
        on(host, "[data-check-uid]", "change", event => {
          const uid = Number(event.currentTarget.dataset.checkUid);
          if (event.currentTarget.checked) accountsState.checkedUids.add(uid);
          else accountsState.checkedUids.delete(uid);
          syncAccountSelection(host);
        });
        $("#acc-select-filtered", host).addEventListener("click", () => {
          accountsState.checkedUids = new Set(rows.map(account => account.uid));
          syncAccountSelection(host);
        });
        $("#acc-clear-selection", host).addEventListener("click", () => {
          accountsState.checkedUids.clear();
          syncAccountSelection(host);
        });
        syncAccountSelection(host);
        $("#acc-bulk-delete", host).addEventListener("click", async event => {
          const uids = [...accountsState.checkedUids];
          const preview = accountsState.list.filter(a => accountsState.checkedUids.has(a.uid)).map(a => `${a.uid} (${a.username})`).join(", ");
          if (!await confirmDialog({ title: t("accounts.bulk_delete", { count: uids.length }), message: t("accounts.bulk_confirm", { accounts: preview }), danger: true, requireText: "DELETE", confirmLabel: t("common.delete") })) return;
          await run(event.currentTarget, async () => {
            const data = await api("/admin/api/accounts/bulk-delete", { method: "POST", body: { uids, confirm: "DELETE" } });
            accountsState.checkedUids = new Set(data.failed.map(item => item.uid));
            await refreshAccounts();
            toast(t("accounts.bulk_result", { deleted: data.deleted_uids.length, missing: data.missing_uids.length, failed: data.failed.length }), data.failed.length ? "bad" : "good");
            if (data.failed.length) await confirmDialog({ title: t("accounts.bulk_failures"), message: data.failed.map(item => `${item.uid}: ${item.message}`).join("; ") });
          });
        });
        on(host, "[data-acc-page]", "click", event => {
          accountsState.page = Number(event.currentTarget.dataset.accPage);
          renderAccountResults();
        });
      }

      // Ticking a checkbox must not rebuild the table: replacing the rows resets the scroll position and
      // drops the pointer, so only the selection-dependent bits are updated in place.
      function syncAccountSelection(host) {
        const checkboxes = $$("[data-check-uid]", host);
        const pageUids = checkboxes.map(box => Number(box.dataset.checkUid));
        for (const box of checkboxes) box.checked = accountsState.checkedUids.has(Number(box.dataset.checkUid));

        const selectPage = $("#acc-select-page", host);
        if (selectPage) {
          selectPage.checked = pageUids.length > 0 && pageUids.every(uid => accountsState.checkedUids.has(uid));
          selectPage.indeterminate = !selectPage.checked && pageUids.some(uid => accountsState.checkedUids.has(uid));
        }

        const bulkDelete = $("#acc-bulk-delete", host);
        if (bulkDelete) {
          bulkDelete.disabled = !accountsState.checkedUids.size || accountsState.checkedUids.size > 500;
          bulkDelete.textContent = t("accounts.bulk_delete", { count: accountsState.checkedUids.size });
        }
      }

      function accountStateChips(account) {
        const chips = [];
        if (account.online) chips.push(chip(t("common.online"), "good"));
        if (account.banned) chips.push(chip(t("common.banned"), "bad"));
        if (!account.email_verified) chips.push(chip(t("accounts.chip_unverified"), "warn"));
        if (!account.has_password) chips.push(chip(t("accounts.chip_no_password"), "warn"));
        if (!chips.length) chips.push(chip(t("common.ok"), "subtle"));
        return html`<span class="chip-list">${chips}</span>`;
      }

      function exportAccountsCsv() {
        const rows = filteredAccounts();
        const header = ["uid", "username", "email", "role", "nickname", "level", "world_level", "email_verified", "has_password", "banned", "ban_reason", "online", "permissions", "registration_ip", "last_ip", "current_ip", "created_at", "last_seen_at"];
        const escapeCsv = value => `"${String(value ?? "").replaceAll('"', '""')}"`;
        const lines = [header.join(",")];
        for (const account of rows) {
          lines.push(header.map(key => escapeCsv(account[key])).join(","));
        }
        downloadBlob(`accounts-${new Date().toISOString().slice(0, 10)}.csv`, new Blob(["﻿" + lines.join("\n")], { type: "text/csv;charset=utf-8" }));
      }

      function roleOptions(selectedRole) {
        const roles = accountsState.roles.length ? accountsState.roles : [accountsState.defaultRole];
        return roles.map(role => option(role, role, selectedRole));
      }

      function renderAccountEditor() {
        const host = $("#acc-editor");
        if (!host) return;

        if (accountsState.mode === "create") {
          host.innerHTML = String(renderCreateAccount());
          bindCreateAccount(host);
          return;
        }

        const account = selectedAccount();
        if (!account) {
          host.innerHTML = String(html`
            <div class="panel-head"><h3>${t("accounts.workspace")}</h3></div>
            ${emptyState(t("accounts.select_hint"))}`);
          return;
        }

        const tabs = ["profile", "access", "security", "player", "mail", "danger"];
        host.classList.toggle("danger-zone", accountsState.tab === "danger");
        host.innerHTML = String(html`
          <div class="panel-head">
            <div>
              <h3>${account.username || `UID ${account.uid}`}</h3>
              <div class="muted small">UID ${account.uid} &middot; ${account.email}</div>
            </div>
            <span class="chip-list">
              ${chip(account.online ? t("common.online") : t("common.offline"), account.online ? "good" : "subtle")}
              ${account.banned ? chip(t("common.banned"), "bad") : ""}
            </span>
          </div>
          <div class="tabs" role="tablist">
            ${tabs.map(tab => html`<button class="tab ${accountsState.tab === tab ? "active" : ""} ${tab === "danger" ? "tab-danger" : ""}" type="button" role="tab" data-tab="${tab}">${t(`accounts.tab.${tab}`)}</button>`)}
          </div>
          <div class="panel-body">${renderAccountTab(account)}</div>`);

        on(host, "[data-tab]", "click", event => {
          accountsState.tab = event.currentTarget.dataset.tab;
          renderAccountEditor();
        });
        bindAccountTab(host, account);
      }

      function renderCreateAccount() {
        return html`
          <div class="panel-head">
            <h3>${t("accounts.create_title")}</h3>
            <button id="acc-cancel-create" class="btn ghost small" type="button">${t("common.cancel")}</button>
          </div>
          <div class="panel-body">
            <form id="acc-create-form" class="form-grid">
              <label class="field"><span>${t("common.username")}</span><input id="create-username" required maxlength="64" autocomplete="off"></label>
              <label class="field"><span>${t("common.email")}</span><input id="create-email" type="email" required maxlength="254" autocomplete="off"></label>
              <label class="field">
                <span>${t("common.password")}</span>
                <div class="input-row">
                  <input id="create-password" type="text" required autocomplete="new-password" spellcheck="false">
                  <button class="btn ghost small" type="button" id="create-generate">${t("accounts.generate")}</button>
                </div>
              </label>
              <label class="field"><span>UID</span><input id="create-uid" type="number" min="1" placeholder="${t("accounts.uid_auto")}"></label>
              <label class="field"><span>${t("common.role")}</span><select id="create-role">${roleOptions(accountsState.defaultRole)}</select></label>
              <div class="field"><span>${t("accounts.email_verified")}</span>${toggleSwitch("create-email-verified", false, t("accounts.mark_verified"))}</div>
              <label class="field wide"><span>${t("accounts.permission_overrides")}</span><textarea id="create-permissions" spellcheck="false" placeholder="${t("accounts.permissions_placeholder")}"></textarea></label>
              <div class="actions wide">
                <button class="btn primary" type="submit">${t("accounts.create")}</button>
              </div>
            </form>
          </div>`;
      }

      function bindCreateAccount(host) {
        $("#acc-cancel-create", host).addEventListener("click", () => {
          accountsState.mode = "detail";
          renderAccountResults();
          renderAccountEditor();
        });
        $("#create-generate", host).addEventListener("click", () => {
          $("#create-password", host).value = generatePassword();
        });
        $("#acc-create-form", host).addEventListener("submit", async event => {
          event.preventDefault();
          const button = $("button[type=submit]", event.currentTarget);
          const uidValue = $("#create-uid", host).value.trim();
          await run(button, async () => {
            const data = await api("/admin/api/accounts", {
              method: "POST",
              body: {
                username: $("#create-username", host).value.trim(),
                email: $("#create-email", host).value.trim(),
                password: $("#create-password", host).value,
                uid: uidValue ? Number(uidValue) : null,
                role: $("#create-role", host).value,
                permissions: $("#create-permissions", host).value,
                email_verified: $("#create-email-verified", host).checked
              }
            });
            accountsState.mode = "detail";
            accountsState.tab = "profile";
            await refreshAccounts(data.account?.uid ?? null);
          }, t("accounts.created"));
        });
      }

      function generatePassword(length = 16) {
        const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        const values = new Uint32Array(length);
        window.crypto.getRandomValues(values);
        return Array.from(values, value => alphabet[value % alphabet.length]).join("");
      }

      function renderAccountTab(account) {
        switch (accountsState.tab) {
          case "access": return renderAccountAccessTab(account);
          case "security": return renderAccountSecurityTab(account);
          case "player": return renderAccountPlayerTab(account);
          case "mail": return renderAccountMailTab(account);
          case "danger": return renderAccountDangerTab(account);
          default: return renderAccountProfileTab(account);
        }
      }

      function renderAccountProfileTab(account) {
        return html`
          <form id="acc-profile-form" class="form-grid">
            <label class="field"><span>${t("common.username")}</span><input id="edit-username" value="${account.username}" required maxlength="64" autocomplete="off"></label>
            <label class="field"><span>${t("common.email")}</span><input id="edit-email" type="email" value="${account.email}" required maxlength="254" autocomplete="off"></label>
            <label class="field"><span>${t("common.role")}</span><select id="edit-role">${roleOptions(account.role || accountsState.defaultRole)}</select></label>
            <div class="field"><span>${t("accounts.email_verified")}</span>${toggleSwitch("edit-email-verified", account.email_verified, account.email_verified ? t("accounts.verified_at", { time: fmt.unix(account.email_verified_at) }) : t("accounts.not_verified"))}</div>
            <div class="wide kv-grid">
              ${kv("UID", account.uid, { mono: true })}
              ${kv(t("common.nickname"), account.nickname || t("common.none"))}
              ${kv(t("accounts.trailblaze_level"), account.level ?? t("common.none"))}
              ${kv(t("sessions.world_level"), account.world_level ?? 0)}
              ${kv(t("common.password"), account.has_password ? t("accounts.password_set") : t("accounts.password_missing"))}
              ${kv(t("accounts.dispatch_token"), fmt.unix(account.dispatch_token_expires_at))}
              ${kv(t("accounts.combo_token"), fmt.unix(account.combo_token_expires_at))}
              ${kv(t("accounts.identity_keys"), account.identity_keys)}
              ${kv(t("accounts.registration_ip"), account.registration_ip || t("blocks.no_records"), { mono: true })}
              ${kv(t("accounts.last_ip"), account.last_ip || t("blocks.no_records"), { mono: true })}
              ${kv(t("accounts.current_ip"), account.current_ip || t("common.offline"), { mono: true })}
              ${kv(t("accounts.created_at"), account.created_at ? fmt.unix(account.created_at) : t("blocks.no_records"))}
            </div>
            <div class="actions wide">
              <button class="btn primary" type="submit">${t("accounts.save_profile")}</button>
              <button class="btn ghost" type="button" data-open-command="${account.uid}" ${account.online ? "" : "disabled"}>${t("common.run_command")}</button>
              <button class="btn ghost" type="button" data-open-mail="${account.uid}">${t("accounts.compose_mail")}</button>
            </div>
          </form>`;
      }

      function renderAccountAccessTab(account) {
        return html`
          <form id="acc-access-form" class="form-grid">
            <label class="field"><span>${t("common.role")}</span><select id="access-role">${roleOptions(account.role || accountsState.defaultRole)}</select></label>
            <div class="field"><span>${t("accounts.base_permissions")}</span>${permissionChips(account.base_permissions)}</div>
            <label class="field wide">
              <span>${t("accounts.permission_overrides")}</span>
              <textarea id="access-permissions" spellcheck="false" placeholder="${t("accounts.permissions_placeholder")}">${account.permissions || ""}</textarea>
            </label>
            <div class="field wide">
              <span>${t("accounts.known_permissions")}</span>
              <span class="chip-list">${(config.knownPermissions || []).map(permission => html`<button type="button" class="chip clickable" data-add-permission="${permission}">${permission}</button>`)}</span>
            </div>
            <div class="field wide"><span>${t("accounts.effective_permissions")}</span>${permissionChips(account.effective_permissions)}</div>
            <div class="actions wide">
              <button class="btn primary" type="submit">${t("accounts.save_access")}</button>
            </div>
          </form>`;
      }

      function renderAccountSecurityTab(account) {
        return html`
          <div class="stack">
            <form id="acc-password-form" class="form-grid">
              <label class="field">
                <span>${t("accounts.new_password")}</span>
                <div class="input-row">
                  <input id="edit-password" type="text" autocomplete="new-password" spellcheck="false">
                  <button class="btn ghost small" type="button" id="edit-generate">${t("accounts.generate")}</button>
                </div>
              </label>
              <div class="field"><span>${t("accounts.after_change")}</span>${toggleSwitch("edit-revoke-tokens", true, t("accounts.revoke_tokens_too"))}</div>
              <div class="actions wide">
                <button class="btn primary" type="submit">${t("accounts.set_password")}</button>
                <button id="acc-send-verification" class="btn ghost" type="button">${t("accounts.send_verification")}</button>
                <button id="acc-revoke-tokens" class="btn ghost" type="button">${t("accounts.revoke_tokens")}</button>
                <button id="acc-kick" class="btn ghost" type="button" ${account.online ? "" : "disabled"}>${t("accounts.kick_session")}</button>
                <button id="acc-clear-identity" class="btn ghost" type="button" ${account.identity_keys ? "" : "disabled"}>${t("accounts.clear_identity", { count: account.identity_keys })}</button>
              </div>
            </form>

            ${renderAccountIdentityRecords(account)}
            <form id="acc-ban-form" class="form-grid">
              <div class="wide section-title">${t("accounts.ban_section")}</div>
              ${account.banned ? html`
                <div class="wide notice bad">
                  <strong>${account.ban_permanent ? t("accounts.ban_permanent_active") : t("accounts.ban_timed_active", { remaining: fmt.duration(account.ban_remaining_seconds), until: fmt.unix(account.ban_expires_at) })}</strong>
                  <div>${t("common.reason")}: ${account.ban_reason || t("common.none")} &middot; ${t("accounts.ban_since")}: ${fmt.unix(account.ban_created_at)}</div>
                </div>` : ""}
              <label class="field wide"><span>${t("accounts.ban_reason")}</span><textarea id="ban-reason" spellcheck="false" placeholder="${t("accounts.ban_reason_placeholder")}">${account.ban_reason || ""}</textarea></label>
              <div class="field"><span>${t("accounts.ban_type")}</span>${toggleSwitch("ban-permanent", !account.banned || account.ban_permanent !== false, t("accounts.ban_permanent"))}</div>
              <label class="field">
                <span>${t("accounts.ban_duration")}</span>
                <div class="input-row">
                  <input id="ban-duration" placeholder="7d" ${!account.banned || account.ban_permanent !== false ? "disabled" : ""}>
                  ${["1h", "1d", "7d", "30d"].map(preset => html`<button class="btn ghost small" type="button" data-ban-preset="${preset}">${preset}</button>`)}
                </div>
              </label>
              <div class="actions wide">
                <button class="btn danger" type="submit">${account.banned ? t("accounts.update_ban") : t("accounts.ban")}</button>
                <button id="acc-unban" class="btn ghost" type="button" ${account.banned ? "" : "disabled"}>${t("accounts.unban")}</button>
              </div>
            </form>
          </div>`;
      }

      function renderAccountPlayerTab(account) {
        const player = accountsState.player;
        if (player.uid !== account.uid || (!player.data && !player.loading && !player.error)) {
          ensurePlayerDetail(account.uid);
          return loadingState();
        }
        if (player.loading) return loadingState();
        if (player.error) return html`<div class="empty bad">${player.error}</div>`;
        const data = player.data;
        if (!data.exists) return emptyState(t("accounts.player_missing"));

        return html`
          <div class="stack">
            <div class="metric-grid compact">
              ${metric(t("accounts.trailblaze_level"), data.level, { hint: `${t("common.exp")} ${fmt.number(data.exp)}` })}
              ${metric(t("sessions.world_level"), data.world_level)}
              ${metric(t("player.credits"), fmt.number(data.scoin))}
              ${metric(t("player.jade"), fmt.number(data.hcoin))}
              ${metric(t("player.shards"), fmt.number(data.mcoin))}
              ${metric(t("player.stamina"), `${data.stamina}`, { hint: `${t("player.reserve")} ${data.stamina_reserve}` })}
            </div>
            <div class="kv-grid">
              ${kv(t("common.nickname"), data.name || t("common.none"))}
              ${kv(t("player.signature"), data.signature || t("common.none"))}
              ${kv(t("player.status"), html`<span class="chip-list">${chip(data.online ? t("common.online") : t("common.offline"), data.online ? "good" : "subtle")}${data.in_battle ? chip(t("sessions.battle"), "warn") : ""}${data.in_raid ? chip(t("sessions.raid"), "accent") : ""}</span>`)}
              ${kv(t("sessions.scene"), `${data.plane_id} / ${data.floor_id} / ${data.entry_id}`, { mono: true })}
              ${kv(t("sessions.last_active"), data.last_active ? `${fmt.unix(data.last_active)} (${fmt.relative(data.last_active)})` : t("common.never"))}
              ${kv(t("player.month_card"), data.month_card_expire > 0 ? fmt.unix(data.month_card_expire) : t("common.none"))}
              ${kv(t("player.fake_time"), data.fake_time_date > 0 ? data.fake_time_date : t("common.off"))}
              ${kv(t("player.head_icon"), data.head_icon, { mono: true })}
              ${kv(t("player.language"), data.language || t("common.none"))}
              ${kv(t("player.missions"), t("player.missions_value", { finished: data.finished_main_missions, running: data.running_main_missions }))}
              ${kv(t("player.inventory"), t("player.inventory_value", { materials: data.material_count, equipment: data.equipment_count, relics: data.relic_count }))}
              ${kv(t("accounts.tab.mail"), data.mail_count)}
            </div>
            <div class="section-title">${t("sessions.lineup")}</div>
            ${data.lineup?.length ? html`<span class="chip-list">${data.lineup.map(id => chip(handbook.label("Avatar", id), "accent"))}</span>` : emptyState(t("common.none"))}
            <div class="section-title">${t("player.avatars", { count: data.avatar_count })}</div>
            ${data.avatars?.length ? html`
              <div class="table-wrap short">
                <table>
                  <thead><tr><th class="w-uid">ID</th><th>${t("common.name")}</th><th class="w-sm">${t("common.level")}</th><th class="w-sm">${t("player.promotion")}</th><th class="w-sm">${t("player.eidolon")}</th></tr></thead>
                  <tbody>${data.avatars.map(avatar => html`<tr><td class="mono">${avatar.id}</td><td>${handbook.name("Avatar", avatar.id) || html`<span class="muted">-</span>`}</td><td>${avatar.level}</td><td>${avatar.promotion}</td><td>${avatar.rank}</td></tr>`)}</tbody>
                </table>
              </div>` : emptyState(t("common.none"))}
            <div class="actions">
              <button class="btn ghost" type="button" id="acc-player-refresh">${t("common.refresh")}</button>
              <button class="btn ghost" type="button" data-open-command="${account.uid}" ${data.online ? "" : "disabled"}>${t("common.run_command")}</button>
              <button class="btn ghost" type="button" data-open-mail="${account.uid}">${t("accounts.compose_mail")}</button>
            </div>
          </div>`;
      }

      function ensurePlayerDetail(uid, force) {
        if (!force && accountsState.player.uid === uid && (accountsState.player.loading || accountsState.player.data)) return;
        accountsState.player = { uid, data: null, loading: true, error: "" };
        api(`/admin/api/players/${uid}`).then(data => {
          if (accountsState.player.uid !== uid) return;
          accountsState.player = { uid, data, loading: false, error: "" };
          if (accountsState.tab === "player" && accountsState.selectedUid === uid) renderAccountEditor();
        }).catch(error => {
          if (accountsState.player.uid !== uid) return;
          accountsState.player = { uid, data: null, loading: false, error: errorMessage(error) };
          if (accountsState.tab === "player" && accountsState.selectedUid === uid) renderAccountEditor();
        });
        handbook.ensureDefault().then(book => {
          if (book && accountsState.tab === "player" && accountsState.selectedUid === uid && !accountsState.player.loading) renderAccountEditor();
        });
      }

      function renderAccountMailTab(account) {
        const mailbox = accountsState.mailbox;
        if (mailbox.uid !== account.uid || (!mailbox.data && !mailbox.loading && !mailbox.error)) {
          ensureMailbox(account.uid);
          return loadingState();
        }
        if (mailbox.loading) return loadingState();
        if (mailbox.error) return html`<div class="empty bad">${mailbox.error}</div>`;
        const mails = mailbox.data.mails || [];
        return html`
          <div class="stack">
            <div class="row space-between">
              <span class="muted">${t("mail.mailbox_count", { count: mails.length })}</span>
              <div class="row">
                <button class="btn ghost small" type="button" id="acc-mail-refresh">${t("common.refresh")}</button>
                <button class="btn primary small" type="button" data-open-mail="${account.uid}">${t("accounts.compose_mail")}</button>
              </div>
            </div>
            ${mails.length ? html`
              <div class="mail-list">
                ${mails.map(mail => html`
                  <article class="mail-item ${mail.is_read ? "read" : ""}">
                    <div class="mail-head">
                      <div>
                        <strong>${mail.title}</strong>
                        <div class="muted small">${mail.sender} &middot; #${mail.mail_id} &middot; ${fmt.unix(mail.send_time)} &rarr; ${fmt.unix(mail.expire_time)}</div>
                      </div>
                      <div class="row">
                        ${mail.is_star ? chip(t("mail.star"), "accent") : ""}
                        ${chip(mail.is_read ? t("mail.read") : t("mail.unread"), mail.is_read ? "subtle" : "good")}
                        <button class="btn danger small" type="button" data-delete-mail="${mail.mail_id}">${t("common.delete")}</button>
                      </div>
                    </div>
                    <p class="mail-content">${mail.content}</p>
                    ${mail.attachments?.length ? html`<span class="chip-list">${mail.attachments.map(item => chip(`${handbook.label("Item", item.item_id)} × ${item.count}`, "subtle"))}</span>` : ""}
                  </article>`)}
              </div>` : emptyState(t("mail.mailbox_empty"))}
          </div>`;
      }

      function ensureMailbox(uid, force) {
        if (!force && accountsState.mailbox.uid === uid && (accountsState.mailbox.loading || accountsState.mailbox.data)) return;
        accountsState.mailbox = { uid, data: null, loading: true, error: "" };
        api(`/admin/api/players/${uid}/mail`).then(data => {
          if (accountsState.mailbox.uid !== uid) return;
          accountsState.mailbox = { uid, data, loading: false, error: "" };
          if (accountsState.tab === "mail" && accountsState.selectedUid === uid) renderAccountEditor();
        }).catch(error => {
          if (accountsState.mailbox.uid !== uid) return;
          accountsState.mailbox = { uid, data: null, loading: false, error: errorMessage(error) };
          if (accountsState.tab === "mail" && accountsState.selectedUid === uid) renderAccountEditor();
        });
        handbook.ensureDefault().then(book => {
          if (book && accountsState.tab === "mail" && accountsState.selectedUid === uid && !accountsState.mailbox.loading) renderAccountEditor();
        });
      }

      function renderAccountDangerTab(account) {
        return html`
          <div class="stack">
            <div class="section-title">${t("accounts.test_tools")}</div>
            <p class="muted">${t("accounts.test_tools_hint")}</p>
            <div class="actions">
              <button class="btn ghost" type="button" id="acc-clone">${t("accounts.clone")}</button>
              <button class="btn ghost" type="button" id="acc-snapshot-save">${t("snapshots.save")}</button>
            </div>
            <div class="danger-card">
              <div>
                <strong>${t("snapshots.restore_title")}</strong>
                <p class="muted">${t("snapshots.restore_message")}</p>
              </div>
              <button class="btn danger" type="button" id="acc-snapshot-restore">${t("snapshots.restore")}</button>
            </div>
            <div class="danger-card">
              <div>
                <strong>${t("story.jump_title")}</strong>
                <p class="muted">${t("story.jump_message")}</p>
              </div>
              <button class="btn danger" type="button" id="acc-story-jump">${t("story.jump")}</button>
            </div>
            <div class="danger-card">
              <div>
                <strong>${t("accounts.reset_title")}</strong>
                <p class="muted">${t("accounts.reset_message")}</p>
              </div>
              <button class="btn danger" type="button" id="acc-reset">${t("accounts.reset")}</button>
            </div>
            <div class="danger-card">
              <div>
                <strong>${t("accounts.delete_title")}</strong>
                <p class="muted">${t("accounts.delete_message")}</p>
              </div>
              <button class="btn danger" type="button" id="acc-delete">${t("accounts.delete")}</button>
            </div>
          </div>`;
      }

      function bindAccountTab(host, account) {
        on(host, "[data-block-kind]", "click", event => {
          const data = event.currentTarget.dataset;
          navigate("blocks", { kind: data.blockKind, ...(data.blockKey ? { identity_key: data.blockKey } : { value: data.blockValue }) });
        });
        on(host, "[data-open-blocks]", "click", () => navigate("blocks"));
        on(host, "[data-open-command]", "click", event => navigate("commands", { target: event.currentTarget.dataset.openCommand }));
        on(host, "[data-open-mail]", "click", event => navigate("mail", { uid: event.currentTarget.dataset.openMail }));

        const profileForm = $("#acc-profile-form", host);
        if (profileForm) {
          profileForm.addEventListener("submit", event => {
            event.preventDefault();
            saveAccount(account, $("button[type=submit]", profileForm), {
              username: $("#edit-username", host).value.trim(),
              email: $("#edit-email", host).value.trim(),
              role: $("#edit-role", host).value,
              permissions: account.permissions || "",
              email_verified: $("#edit-email-verified", host).checked
            }, t("accounts.profile_saved"));
          });
        }

        const accessForm = $("#acc-access-form", host);
        if (accessForm) {
          on(host, "[data-add-permission]", "click", event => {
            const textarea = $("#access-permissions", host);
            const permission = event.currentTarget.dataset.addPermission;
            const current = textarea.value.split(/[,\s;]+/).filter(Boolean);
            if (!current.includes(permission)) current.push(permission);
            textarea.value = current.join(",");
          });
          accessForm.addEventListener("submit", event => {
            event.preventDefault();
            saveAccount(account, $("button[type=submit]", accessForm), {
              username: account.username,
              email: account.email,
              role: $("#access-role", host).value,
              permissions: $("#access-permissions", host).value,
              email_verified: account.email_verified
            }, t("accounts.access_saved"));
          });
        }

        const passwordForm = $("#acc-password-form", host);
        if (passwordForm) {
          $("#edit-generate", host).addEventListener("click", () => {
            $("#edit-password", host).value = generatePassword();
          });
          passwordForm.addEventListener("submit", async event => {
            event.preventDefault();
            const password = $("#edit-password", host).value;
            if (!password) {
              toast(t("accounts.password_required"), "bad");
              return;
            }
            await run($("button[type=submit]", passwordForm), async () => {
              await api(`/admin/api/accounts/${account.uid}/password`, {
                method: "PUT",
                body: { password, revoke_tokens: $("#edit-revoke-tokens", host).checked }
              });
              await refreshAccounts(account.uid);
            }, t("accounts.password_updated"));
          });
          $("#acc-send-verification", host).addEventListener("click", event => run(event.currentTarget, async () => {
            const data = await api(`/admin/api/accounts/${account.uid}/email/verification`, { method: "POST" });
            await refreshAccounts(account.uid);
            if (!data.email_sent) throw new Error(t("accounts.verification_failed"));
          }, t("accounts.verification_sent")));
          $("#acc-revoke-tokens", host).addEventListener("click", event => run(event.currentTarget, async () => {
            await api(`/admin/api/accounts/${account.uid}/tokens/revoke`, { method: "POST" });
            await refreshAccounts(account.uid);
          }, t("accounts.tokens_revoked")));
          $("#acc-kick", host).addEventListener("click", event => run(event.currentTarget, async () => {
            await api(`/admin/api/accounts/${account.uid}/session/kick`, { method: "POST" });
            await refreshAccounts(account.uid);
          }, t("sessions.kicked")));
          $("#acc-clear-identity", host).addEventListener("click", async event => {
            if (!await confirmDialog({ title: t("accounts.clear_identity_title"), message: t("accounts.clear_identity_message"), danger: true })) return;
            await run(event.currentTarget, async () => {
              await api(`/admin/api/accounts/${account.uid}/identity-keys`, { method: "DELETE" });
              await refreshAccounts(account.uid);
            }, t("accounts.identity_cleared"));
          });
        }

        const banForm = $("#acc-ban-form", host);
        if (banForm) {
          const permanent = $("#ban-permanent", host);
          const duration = $("#ban-duration", host);
          permanent.addEventListener("change", () => {
            duration.disabled = permanent.checked;
            if (!permanent.checked) duration.focus();
          });
          on(host, "[data-ban-preset]", "click", event => {
            permanent.checked = false;
            duration.disabled = false;
            duration.value = event.currentTarget.dataset.banPreset;
          });
          banForm.addEventListener("submit", async event => {
            event.preventDefault();
            const isPermanent = permanent.checked;
            if (!isPermanent && !duration.value.trim()) {
              toast(t("accounts.ban_duration_required"), "bad");
              return;
            }
            if (!await confirmDialog({ title: t("accounts.ban_confirm_title", { user: account.username }), message: isPermanent ? t("accounts.ban_confirm_permanent") : t("accounts.ban_confirm_timed", { duration: duration.value.trim() }), danger: true, confirmLabel: t("accounts.ban") })) return;
            await run($("button[type=submit]", banForm), async () => {
              await api(`/admin/api/accounts/${account.uid}/ban`, {
                method: "POST",
                body: { reason: $("#ban-reason", host).value.trim(), permanent: isPermanent, duration: isPermanent ? null : duration.value.trim() }
              });
              await refreshAccounts(account.uid);
            }, t("accounts.ban_updated"));
          });
          $("#acc-unban", host).addEventListener("click", event => run(event.currentTarget, async () => {
            await api(`/admin/api/accounts/${account.uid}/ban`, { method: "DELETE" });
            await refreshAccounts(account.uid);
          }, t("accounts.unbanned")));
        }

        const playerRefresh = $("#acc-player-refresh", host);
        if (playerRefresh) {
          playerRefresh.addEventListener("click", () => {
            ensurePlayerDetail(account.uid, true);
            renderAccountEditor();
          });
        }

        const mailRefresh = $("#acc-mail-refresh", host);
        if (mailRefresh) {
          mailRefresh.addEventListener("click", () => {
            ensureMailbox(account.uid, true);
            renderAccountEditor();
          });
        }
        on(host, "[data-delete-mail]", "click", async event => {
          const mailId = event.currentTarget.dataset.deleteMail;
          if (!await confirmDialog({ title: t("mail.delete_title", { id: mailId }), danger: true, confirmLabel: t("common.delete") })) return;
          await run(event.currentTarget, async () => {
            const data = await api(`/admin/api/players/${account.uid}/mail/${mailId}`, { method: "DELETE" });
            accountsState.mailbox = { uid: account.uid, data, loading: false, error: "" };
            renderAccountEditor();
          }, t("mail.deleted"));
        });

        const resetButton = $("#acc-reset", host);
        if (resetButton) {
          resetButton.addEventListener("click", async () => {
            if (!await confirmDialog({ title: t("accounts.reset_title"), message: t("accounts.reset_confirm", { user: account.username, uid: account.uid }), danger: true, requireText: "RESET", confirmLabel: t("accounts.reset") })) return;
            await run(resetButton, async () => {
              await api(`/admin/api/accounts/${account.uid}/gameplay/reset`, { method: "POST" });
              await refreshAccounts(account.uid);
            }, t("accounts.reset_done"));
          });
        }
        const deleteButton = $("#acc-delete", host);
        if (deleteButton) {
          deleteButton.addEventListener("click", async () => {
            if (!await confirmDialog({ title: t("accounts.delete_title"), message: t("accounts.delete_confirm", { user: account.username, uid: account.uid }), danger: true, requireText: "DELETE", confirmLabel: t("accounts.delete") })) return;
            await run(deleteButton, async () => {
              await api(`/admin/api/accounts/${account.uid}`, { method: "DELETE" });
              accountsState.tab = "profile";
              await refreshAccounts(null);
            }, t("accounts.deleted"));
          });
        }

        const cloneButton = $("#acc-clone", host);
        if (cloneButton) {
          cloneButton.addEventListener("click", async () => {
            const values = await formDialog({ title: t("accounts.clone_title", { user: account.username, uid: account.uid }), message: t("accounts.clone_message"), fields: testAccountFields(), confirmLabel: t("accounts.clone") });
            if (!values) return;
            await run(cloneButton, async () => {
              const data = await api(`/admin/api/accounts/${account.uid}/clone`, { method: "POST", body: testAccountBody(values) });
              await showTestAccount(data);
            });
          });
        }
        const saveSnapshotButton = $("#acc-snapshot-save", host);
        if (saveSnapshotButton) {
          saveSnapshotButton.addEventListener("click", async () => {
            const stamp = new Date().toISOString().slice(0, 16).replace(/[-:]/g, "").replace("T", "-");
            const values = await formDialog({
              title: t("snapshots.save_title", { user: account.username, uid: account.uid }),
              message: t("snapshots.save_message"),
              fields: [
                { name: "name", label: t("snapshots.name"), value: `${account.username}-${stamp}`, required: true },
                { name: "description", label: t("snapshots.description"), type: "textarea" }
              ],
              confirmLabel: t("snapshots.save")
            });
            if (!values) return;
            await run(saveSnapshotButton, async () => {
              const data = await api("/admin/api/snapshots", { method: "POST", body: { uid: account.uid, name: values.name.trim(), description: values.description.trim() } });
              return data.snapshot.id;
            }, id => t("snapshots.saved", { id }));
          });
        }
        const storyJumpButton = $("#acc-story-jump", host);
        if (storyJumpButton) storyJumpButton.addEventListener("click", () => openStoryDialog(account));
        const restoreButton = $("#acc-snapshot-restore", host);
        if (restoreButton) {
          restoreButton.addEventListener("click", async () => {
            const snapshots = await run(restoreButton, async () => (await api("/admin/api/snapshots")).snapshots || []);
            if (!snapshots) return;
            if (!snapshots.length) {
              toast(t("snapshots.empty"), "bad");
              return;
            }
            const values = await formDialog({
              title: t("snapshots.restore_title"),
              fields: [{ name: "id", label: t("snapshots.pick"), type: "select", value: snapshots[0].id, options: snapshots.map(snapshot => [snapshot.id, `${snapshot.name} (UID ${snapshot.source_uid}, ${fmt.unix(snapshot.created_at)})`]) }],
              confirmLabel: t("snapshots.restore"),
              danger: true
            });
            if (!values) return;
            if (!await confirmDialog({ title: t("snapshots.restore_title"), message: t("snapshots.restore_confirm", { user: account.username, uid: account.uid, id: values.id }), danger: true, requireText: "RESTORE", confirmLabel: t("snapshots.restore") })) return;
            await run(restoreButton, async () => {
              await api(`/admin/api/accounts/${account.uid}/snapshot/restore`, { method: "POST", body: { id: values.id } });
              await refreshAccounts(account.uid);
            }, t("snapshots.restored"));
          });
        }
      }

      // A modal owned by the accounts module. close(result) hands result to onClose; Escape and the backdrop close
      // with no result.
      function accountModal(content, options = {}) {
        const host = $("#modal-host");
        host.innerHTML = String(html`
          <div class="modal-backdrop">
            <div class="modal ${options.wide === false ? "" : "wide"}" role="dialog" aria-modal="true">${content}</div>
          </div>`);
        const element = $(".modal", host);
        let open = true;
        const close = result => {
          if (!open) return;
          open = false;
          host.innerHTML = "";
          document.removeEventListener("keydown", onKey);
          options.onClose?.(result);
        };
        const onKey = event => {
          if (event.key === "Escape") close();
        };
        document.addEventListener("keydown", onKey);
        $(".modal-backdrop", host).addEventListener("click", event => {
          if (event.target === event.currentTarget) close();
        });
        return { element, close };
      }

      // formDialog({ title, message, fields: [{ name, label, type, value, placeholder, options, required }],
      // confirmLabel, danger }) -> Promise<{ [name]: string } | null>
      function formDialog(options) {
        return new Promise(resolve => {
          const modal = accountModal(html`
            <h3>${options.title}</h3>
            ${options.message ? html`<p class="modal-message">${options.message}</p>` : ""}
            <form id="form-dialog" class="stack">
              ${(options.fields || []).map(field => html`
                <label class="field">
                  <span>${field.label}</span>
                  ${field.type === "select"
                    ? html`<select name="${field.name}">${(field.options || []).map(([value, label]) => option(value, label, field.value))}</select>`
                    : field.type === "textarea"
                      ? html`<textarea name="${field.name}" placeholder="${field.placeholder || ""}" spellcheck="false">${field.value || ""}</textarea>`
                      : html`<input name="${field.name}" type="${field.type || "text"}" value="${field.value ?? ""}" placeholder="${field.placeholder || ""}" autocomplete="off" spellcheck="false" ${field.required ? "required" : ""}>`}
                </label>`)}
              <div class="modal-actions">
                <button class="btn ghost" type="button" data-form-cancel>${t("common.cancel")}</button>
                <button class="btn ${options.danger ? "danger" : "primary"}" type="submit">${options.confirmLabel || t("common.confirm")}</button>
              </div>
            </form>`, { wide: false, onClose: result => resolve(result ?? null) });
          const form = $("#form-dialog", modal.element);
          $("[data-form-cancel]", form).addEventListener("click", () => modal.close());
          form.addEventListener("submit", event => {
            event.preventDefault();
            modal.close(Object.fromEntries(new FormData(form).entries()));
          });
          $("input, select, textarea", form)?.focus();
        });
      }

      function testAccountFields() {
        return [
          { name: "username", label: t("common.username"), placeholder: t("accounts.auto_generate") },
          { name: "password", label: t("common.password"), placeholder: t("accounts.auto_generate") },
          { name: "nickname", label: t("accounts.nickname_optional") }
        ];
      }

      function testAccountBody(values) {
        return {
          username: (values.username || "").trim() || null,
          password: values.password || null,
          nickname: (values.nickname || "").trim() || null
        };
      }

      // Selects a freshly created test account and shows its login once (a generated password is not stored anywhere).
      async function showTestAccount(data, extra) {
        const account = data.account;
        accountsState.mode = "detail";
        accountsState.tab = "profile";
        await refreshAccounts(account.uid);
        const lines = [t("accounts.test_ready_user", { username: account.username, uid: account.uid })];
        if (data.generated_password) lines.push(t("accounts.test_ready_password", { password: data.generated_password }));
        if (extra) lines.push("", extra);
        await confirmDialog({ title: t("accounts.test_ready"), message: lines.join("\n"), confirmLabel: t("common.ok") });
      }

      async function openSnapshotsDialog() {
        const modal = accountModal(html`<h3>${t("snapshots.title")}</h3>${loadingState()}`);
        let snapshots;
        try {
          snapshots = (await api("/admin/api/snapshots")).snapshots || [];
        } catch (error) {
          modal.close();
          toast(errorMessage(error), "bad");
          return;
        }
        modal.element.innerHTML = String(html`
          <h3>${t("snapshots.title")}</h3>
          <p class="modal-message">${t("snapshots.hint")}</p>
          ${snapshots.length ? html`
            <div class="mail-list">
              ${snapshots.map(snapshot => html`
                <article class="mail-item">
                  <div class="mail-head">
                    <div>
                      <strong>${snapshot.name}</strong>
                      <div class="muted small">${t("snapshots.meta", { id: snapshot.id, uid: snapshot.source_uid, level: snapshot.player_level, floor: snapshot.floor_id, time: fmt.unix(snapshot.created_at), version: snapshot.game_version })}</div>
                      ${snapshot.frontier_main_mission ? html`<div class="muted small">${t("snapshots.mission")}: ${handbook.label("MainMission", snapshot.frontier_main_mission)}</div>` : ""}
                    </div>
                    <div class="row">
                      <button class="btn primary small" type="button" data-snapshot-spawn="${snapshot.id}">${t("snapshots.spawn")}</button>
                      <button class="btn danger small" type="button" data-snapshot-delete="${snapshot.id}">${t("common.delete")}</button>
                    </div>
                  </div>
                  ${snapshot.description ? html`<p class="mail-content">${snapshot.description}</p>` : ""}
                </article>`)}
            </div>` : emptyState(t("snapshots.empty"))}
          <div class="modal-actions"><button class="btn ghost" type="button" data-snapshot-close>${t("accounts.close")}</button></div>`);
        $("[data-snapshot-close]", modal.element).addEventListener("click", () => modal.close());
        on(modal.element, "[data-snapshot-spawn]", "click", async event => {
          const snapshot = snapshots.find(item => item.id === event.currentTarget.dataset.snapshotSpawn);
          modal.close();
          const values = await formDialog({ title: t("snapshots.spawn_title", { name: snapshot.name }), message: t("accounts.test_account_hint"), fields: testAccountFields(), confirmLabel: t("snapshots.spawn") });
          if (!values) return;
          await run(null, async () => {
            const data = await api(`/admin/api/snapshots/${encodeURIComponent(snapshot.id)}/spawn`, { method: "POST", body: testAccountBody(values) });
            await showTestAccount(data);
          });
        });
        on(modal.element, "[data-snapshot-delete]", "click", async event => {
          const snapshot = snapshots.find(item => item.id === event.currentTarget.dataset.snapshotDelete);
          modal.close();
          if (await confirmDialog({ title: t("snapshots.delete_title", { name: snapshot.name }), danger: true, confirmLabel: t("common.delete") })) {
            await run(null, () => api(`/admin/api/snapshots/${encodeURIComponent(snapshot.id)}`, { method: "DELETE" }), t("snapshots.deleted"));
          }
          await openSnapshotsDialog();
        });
        handbook.ensureDefault();
      }

      // ------------------------------------------------------- story checkpoints
      async function loadStoryCatalog() {
        if (!accountsState.storyCatalog) accountsState.storyCatalog = await api("/admin/api/story/catalog");
        return accountsState.storyCatalog;
      }

      function storyMissionLabel(mission) {
        const name = handbook.name("MainMission", mission.id);
        return [mission.id, name, mission.chapter_name].filter(Boolean).join(" · ");
      }

      // Creates a new account at a story checkpoint, or (account set) resets that account to one.
      async function openStoryDialog(account) {
        const modal = accountModal(html`<h3>${t("story.title")}</h3>${loadingState()}`);
        let catalog;
        try {
          catalog = await loadStoryCatalog();
          await handbook.ensureDefault();
        } catch (error) {
          modal.close();
          toast(errorMessage(error), "bad");
          return;
        }

        const missions = new Map();
        for (const version of catalog.versions) for (const mission of version.main_missions) missions.set(mission.id, mission);
        const form = { version: catalog.versions[0]?.version || "", query: "", main: 0, sub: 0 };

        modal.element.innerHTML = String(html`
          <h3>${account ? t("story.jump_title_for", { user: account.username, uid: account.uid }) : t("story.title")}</h3>
          <p class="modal-message">${account ? t("story.jump_hint") : t("story.hint")}</p>
          <div class="field">
            <span>${t("story.presets")}</span>
            <span class="chip-list">${catalog.presets.map(preset => html`<button type="button" class="chip clickable" data-story-preset="${preset.id}">${t(`story.preset.${preset.id}`)}</button>`)}</span>
          </div>
          <form id="story-form" class="form-grid">
            <label class="field"><span>${t("story.version")}</span><select id="story-version">${catalog.versions.map(version => option(version.version, `${version.version} · ${version.chapters.join(" / ")}`, form.version))}</select></label>
            <label class="field"><span>${t("story.search")}</span><input id="story-search" type="search" placeholder="${t("story.search_placeholder")}" autocomplete="off"></label>
            <label class="field wide"><span>${t("story.main_mission")}</span><select id="story-main" required></select></label>
            <label class="field wide"><span>${t("story.sub_mission")}</span><select id="story-sub"></select></label>
            ${account ? "" : html`
              <label class="field"><span>${t("common.username")}</span><input id="story-username" placeholder="${t("accounts.auto_generate")}" autocomplete="off" spellcheck="false"></label>
              <label class="field">
                <span>${t("common.password")}</span>
                <div class="input-row">
                  <input id="story-password" placeholder="${t("accounts.auto_generate")}" autocomplete="new-password" spellcheck="false">
                  <button class="btn ghost small" type="button" id="story-generate">${t("accounts.generate")}</button>
                </div>
              </label>`}
            <label class="field"><span>${t("story.stop_mode")}</span><select id="story-stop">${option("before", t("story.stop.before"), "before")}${option("after", t("story.stop.after"), "before")}</select></label>
            <label class="field"><span>${t("story.gender")}</span><select id="story-gender">${option("Woman", t("story.gender.woman"), "Woman")}${option("Man", t("story.gender.man"), "Woman")}</select></label>
            <label class="field"><span>${t("story.name")}</span><input id="story-name" maxlength="32" placeholder="${t("story.name_placeholder")}" autocomplete="off"></label>
            <label class="field"><span>${t("story.progression")}</span><select id="story-progression">${option("test_ready", t("story.progression.test_ready"), "test_ready")}${option("live_like", t("story.progression.live_like"), "test_ready")}</select></label>
            <label class="field"><span>${t("story.tutorials")}</span><select id="story-tutorials">${option("finish_prior", t("story.tutorials.finish_prior"), "finish_prior")}${option("keep_open", t("story.tutorials.keep_open"), "finish_prior")}${option("finish_all", t("story.tutorials.finish_all"), "finish_prior")}</select></label>
            <label class="field"><span>${t("story.teleports")}</span><select id="story-teleports">${option("reached_planes", t("story.teleports.reached_planes"), "reached_planes")}${option("all", t("story.teleports.all"), "reached_planes")}${option("none", t("story.teleports.none"), "reached_planes")}</select></label>
            <div class="field"><span>${t("story.extras")}</span>${toggleSwitch("story-currency", false, t("story.grant_currency"))}${toggleSwitch("story-side", false, t("story.include_side"))}</div>
            ${account ? html`<label class="field"><span>${t("common.type_to_confirm", { text: "RESET" })}</span><input id="story-confirm" autocomplete="off"></label>` : ""}
            <div id="story-result" class="wide"></div>
            <div class="modal-actions wide">
              <button class="btn ghost" type="button" data-story-close>${t("accounts.close")}</button>
              <button class="btn ${account ? "danger" : "primary"}" type="submit" id="story-submit" ${account ? "disabled" : ""}>${account ? t("story.jump") : t("story.provision")}</button>
            </div>
          </form>`);

        const root = modal.element;
        const mainSelect = $("#story-main", root);
        const subSelect = $("#story-sub", root);

        function renderMains() {
          const query = form.query.trim().toLowerCase();
          const rows = query
            ? [...missions.values()].filter(mission => storyMissionLabel(mission).toLowerCase().includes(query)).slice(0, 200)
            : catalog.versions.find(version => version.version === form.version)?.main_missions || [];
          if (!rows.some(mission => mission.id === form.main)) form.main = rows[0]?.id || 0;
          mainSelect.innerHTML = String(html`${rows.map(mission => option(mission.id, `${query ? `${mission.version} · ` : ""}${storyMissionLabel(mission)}`, form.main))}`);
          renderSubs();
        }

        function renderSubs() {
          const mission = missions.get(form.main);
          const subs = mission?.sub_missions || [];
          if (!subs.some(sub => sub.id === form.sub)) form.sub = 0;
          subSelect.innerHTML = String(html`${option(0, t("story.no_sub"), form.sub)}${subs.map(sub => option(sub.id, `${sub.id} · ${sub.finish_type}${sub.way_point_floor_id ? ` · ${t("snapshots.floor", { floor: sub.way_point_floor_id })}` : ""}`, form.sub))}`);
        }

        renderMains();
        $("#story-version", root).addEventListener("change", event => {
          form.version = event.currentTarget.value;
          renderMains();
        });
        $("#story-search", root).addEventListener("input", event => {
          form.query = event.currentTarget.value;
          renderMains();
        });
        mainSelect.addEventListener("change", event => {
          form.main = Number(event.currentTarget.value);
          renderSubs();
        });
        subSelect.addEventListener("change", event => {
          form.sub = Number(event.currentTarget.value);
        });
        on(root, "[data-story-preset]", "click", event => {
          const preset = catalog.presets.find(item => item.id === event.currentTarget.dataset.storyPreset);
          const mission = missions.get(preset.main_mission_id);
          form.query = "";
          $("#story-search", root).value = "";
          if (mission) {
            form.version = mission.version;
            $("#story-version", root).value = mission.version;
          }
          form.main = preset.main_mission_id;
          form.sub = preset.sub_mission_id;
          renderMains();
          mainSelect.value = String(form.main);
          renderSubs();
          subSelect.value = String(form.sub);
          $("#story-stop", root).value = preset.stop_mode;
          for (const chip of $$("[data-story-preset]", root)) chip.classList.toggle("accent", chip === event.currentTarget);
        });
        $("[data-story-close]", root).addEventListener("click", () => modal.close());
        $("#story-generate", root)?.addEventListener("click", () => {
          $("#story-password", root).value = generatePassword();
        });
        $("#story-confirm", root)?.addEventListener("input", event => {
          $("#story-submit", root).disabled = event.currentTarget.value.trim() !== "RESET";
        });

        $("#story-form", root).addEventListener("submit", async event => {
          event.preventDefault();
          if (!form.main) {
            toast(t("story.pick_mission"), "bad");
            return;
          }
          const body = {
            target_main_mission_id: form.main,
            target_sub_mission_id: form.sub || null,
            stop_mode: $("#story-stop", root).value,
            trailblazer_gender: $("#story-gender", root).value,
            trailblazer_name: $("#story-name", root).value.trim() || null,
            progression_preset: $("#story-progression", root).value,
            tutorials_mode: $("#story-tutorials", root).value,
            teleports_mode: $("#story-teleports", root).value,
            grant_test_currency: $("#story-currency", root).checked,
            include_side_missions: $("#story-side", root).checked
          };
          if (!account) {
            body.username = $("#story-username", root).value.trim() || null;
            body.password = $("#story-password", root).value || null;
          }
          const result = $("#story-result", root);
          result.innerHTML = String(loadingState(t("story.running")));
          const data = await run($("#story-submit", root), () => api(account ? `/admin/api/accounts/${account.uid}/provision-story` : "/admin/api/accounts/provision-story", { method: "POST", body }));
          if (!data) {
            result.innerHTML = "";
            return;
          }
          result.innerHTML = String(storyReport(data));
          accountsState.mode = "detail";
          accountsState.tab = "profile";
          await refreshAccounts(data.account.uid);
        });
      }

      function storyReport(data) {
        const report = data.report;
        const problems = report.stalled_missions.length + report.warnings.length;
        return html`
          <div class="notice ${report.stalled_missions.length ? "warn" : "good"} stack">
            <strong>${t("story.done", { user: data.account.username, uid: data.account.uid })}</strong>
            ${data.generated_password ? html`<div>${t("accounts.test_ready_password", { password: data.generated_password })}</div>` : ""}
            <div class="kv-grid">
              ${kv(t("story.report.target"), report.target_sub_mission_id ? `${report.target_main_mission_id} / ${report.target_sub_mission_id}` : report.target_main_mission_id, { mono: true })}
              ${kv(t("story.report.tracking"), handbook.label("MainMission", report.tracking_main_mission_id))}
              ${kv(t("story.report.missions"), t("story.report.missions_value", { mains: report.missions_finished, subs: report.sub_missions_finished }))}
              ${kv(t("story.report.spawn"), `${report.spawn_plane_id} / ${report.spawn_floor_id} / ${report.spawn_entry_id}`, { mono: true })}
              ${kv(t("story.report.teleports"), report.teleports_unlocked)}
              ${kv(t("story.report.tutorials"), report.tutorials_marked_finished)}
              ${kv(t("story.report.level"), `${report.player_level} / ${report.world_level}`)}
              ${kv(t("story.report.gender"), t(report.gender_set ? "story.report.gender_set" : "story.report.gender_unset"))}
              ${kv(t("story.report.lineup"), report.lineup.map(id => handbook.label("Avatar", id)).join(", ") || t("common.none"), { mono: true })}
              ${kv(t("story.report.elapsed"), `${fmt.number(report.elapsed_ms)} ms`)}
            </div>
            ${problems || report.bypassed_gates.length ? html`
              <details>
                <summary>${t("story.report.details", { stalled: report.stalled_missions.length, warnings: report.warnings.length, bypassed: report.bypassed_gates.length })}</summary>
                ${report.stalled_missions.length ? html`<div><strong>${t("story.report.stalled")}</strong> <span class="mono">${report.stalled_missions.join(", ")}</span></div>` : ""}
                ${report.warnings.length ? html`<ul>${report.warnings.map(warning => html`<li>${warning}</li>`)}</ul>` : ""}
                ${report.bypassed_gates.length ? html`<div><strong>${t("story.report.bypassed")}</strong><ul>${report.bypassed_gates.map(gate => html`<li class="mono">${gate}</li>`)}</ul></div>` : ""}
              </details>` : ""}
          </div>`;
      }

      async function saveAccount(account, button, payload, successMessage) {
        await run(button, async () => {
          const data = await api(`/admin/api/accounts/${account.uid}`, { method: "PUT", body: payload });
          await refreshAccounts(account.uid);
          if (data.email_sent) toast(t("accounts.verification_sent"), "good");
        }, successMessage);
      }

""";
    }
}
