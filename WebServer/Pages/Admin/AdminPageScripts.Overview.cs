namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string OverviewModule()
    {
        return """
      // ============================================================ overview
      const ICONS = {
        overview: '<svg viewBox="0 0 20 20"><path d="M3 11h5V3H3zm0 6h5v-4H3zm7 0h7V9h-7zm0-14v4h7V3z"/></svg>',
        accounts: '<svg viewBox="0 0 20 20"><path d="M10 10a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zm-6 7c0-3 2.7-5 6-5s6 2 6 5z"/></svg>',
        editor: '<svg viewBox="0 0 20 20"><path d="M3 17h3.5L16 7.5 12.5 4 3 13.5zm8-11.5 3.5 3.5M3 17h14"/></svg>',
        sessions: '<svg viewBox="0 0 20 20"><path d="M2 4h16v9H2zm5 12h6v1.5H7zM10 13v3"/></svg>',
        gridfight: '<svg viewBox="0 0 20 20"><path d="M10 2l7 7-7 9-7-9zm-7 7h14M7 9l3 9 3-9M7 9l3-7 3 7"/></svg>',
        mail: '<svg viewBox="0 0 20 20"><path d="M2 4h16v12H2zm0 0l8 6 8-6"/></svg>',
        announce: '<svg viewBox="0 0 20 20"><path d="M3 8v4h3l6 4V4L6 8zm11 0a3 3 0 0 1 0 4"/></svg>',
        commands: '<svg viewBox="0 0 20 20"><path d="M3 4h14v12H3zm3 3l3 3-3 3m4 0h4"/></svg>',
        access: '<svg viewBox="0 0 20 20"><path d="M10 2l6 3v5c0 4-2.5 6.5-6 8-3.5-1.5-6-4-6-8V5zm-2 8l2 2 3-4"/></svg>',
        settings: '<svg viewBox="0 0 20 20"><path d="M10 6.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zm-6.5 5l-1-1.5 1.7-2.3.4-2.7 2.5-1.2L9 2h2l1.9 1.8 2.5 1.2.4 2.7L17.5 10l-1.7 2.3-.4 2.7-2.5 1.2L11 18H9l-1.9-1.8-2.5-1.2-.4-2.7z"/></svg>',
        hotfix: '<svg viewBox="0 0 20 20"><path d="M4 14a4 4 0 0 1 1-7.9A5 5 0 0 1 14.5 7a3.5 3.5 0 0 1 .5 7zm6-6v7m-3-3l3 3 3-3"/></svg>',
        logs: '<svg viewBox="0 0 20 20"><path d="M4 3h12v14H4zm3 4h6m-6 3h6m-6 3h4"/></svg>',
        maintenance: '<svg viewBox="0 0 20 20"><path d="M12.5 3a4 4 0 0 0-3.7 5.5L3 14.3 5.7 17l5.8-5.8A4 4 0 0 0 17 7.5l-2.5 1-1.6-1.6 1-2.5A4 4 0 0 0 12.5 3z"/></svg>',
        handbook: '<svg viewBox="0 0 20 20"><path d="M4 3h9a3 3 0 0 1 3 3v11H7a3 3 0 0 0-3 3zm0 0v14m3-11h6m-6 3h6"/></svg>',
        warps: '<svg viewBox="0 0 20 20"><path d="M9 2.5Q9.6 8.4 15.5 9 9.6 9.6 9 15.5 8.4 9.6 2.5 9 8.4 8.4 9 2.5zm6.5 10.8q.2 2 2.2 2.2-2 .2-2.2 2.2-.2-2-2.2-2.2 2-.2 2.2-2.2z"/></svg>'
      };

      registerModule({
        id: "overview",
        icon: ICONS.overview,
        poll: 15000,
        async load({ force }) {
          if (!state.cache.overview || force) state.cache.overview = await api("/admin/api/overview");
        },
        async tick() {
          state.cache.overview = await api("/admin/api/overview");
          rerender();
        },
        render(root) {
          renderOverview(root);
        }
      });

      function renderOverview(root) {
        const data = state.cache.overview || {};
        const stats = data.stats || {};
        const server = data.server || {};
        const online = data.online_players || [];
        const attention = data.attention || [];
        const roles = data.role_breakdown || [];
        const usedMb = Math.max(0, (server.machine_total_mb || 0) - (server.machine_available_mb || 0));
        const memoryPercent = server.machine_total_mb > 0 ? Math.round(usedMb / server.machine_total_mb * 100) : 0;
        const maxRole = Math.max(1, ...roles.map(role => role.count));

        root.innerHTML = String(html`
          <div class="metric-grid">
            ${metric(t("overview.accounts"), fmt.number(stats.total_accounts))}
            ${metric(t("overview.online"), fmt.number(stats.online_players), { kind: stats.online_players ? "good" : "", hint: t("overview.connections", { count: fmt.number(stats.connections) }) })}
            ${metric(t("overview.banned"), fmt.number(stats.banned_accounts), { kind: stats.banned_accounts ? "bad" : "" })}
            ${metric(t("overview.unverified"), fmt.number(stats.unverified_email), { kind: stats.unverified_email ? "warn" : "" })}
            ${metric(t("overview.no_password"), fmt.number(stats.password_missing), { kind: stats.password_missing ? "warn" : "" })}
            ${metric(t("overview.roles"), fmt.number(stats.roles))}
          </div>

          <div class="grid two">
            <section class="panel">
              <div class="panel-head">
                <h3>${t("overview.server")}</h3>
                <span class="chip-list">
                  ${server.version ? chip(`v${server.version}`, "subtle") : ""}
                  ${server.public_mode ? chip(t("overview.public_mode"), "warn") : ""}
                </span>
              </div>
              <div class="panel-body kv-grid">
                ${kv(t("overview.name"), server.name || config.serverName)}
                ${kv(t("overview.gateway"), server.game_address, { mono: true })}
                ${kv(t("overview.database"), server.database)}
                ${kv(t("overview.os"), server.os)}
                ${kv(t("overview.uptime"), fmt.duration(server.uptime_seconds))}
                ${kv(t("overview.server_time"), fmt.unix(server.server_time))}
                ${kv(t("overview.process_memory"), fmt.mb(server.process_memory_mb))}
                ${kv(t("overview.last_save"), server.last_save ? fmt.relative(server.last_save) : t("common.never"))}
                ${kv(t("overview.dirty"), fmt.number(server.dirty_uids))}
              </div>
              ${server.machine_total_mb > 0 ? html`
                <div class="panel-body">
                  <div class="bar-label"><span>${t("overview.machine_memory")}</span><span>${fmt.mb(usedMb)} / ${fmt.mb(server.machine_total_mb)}</span></div>
                  <div class="bar"><div class="bar-fill ${memoryPercent > 90 ? "bad" : memoryPercent > 75 ? "warn" : ""}" style="width: ${memoryPercent}%"></div></div>
                </div>` : ""}
            </section>

            <section class="panel">
              <div class="panel-head"><h3>${t("overview.switches")}</h3></div>
              <div class="panel-body stack">
                ${overviewSwitch("registration", t("overview.registration"), server.registration_enabled, t("overview.registration_hint"))}
                ${overviewSwitch("identity", t("overview.device_limit"), server.limit_registration_per_identity, t("overview.device_limit_hint"))}
                ${overviewLink(t("overview.announce"), server.announce_enabled, "announce", {}, t("overview.announce_hint"))}
                ${overviewLink(t("overview.email"), server.email_enabled, "settings", { group: "email" }, t("overview.email_hint"))}
                ${overviewLink(t("overview.fake_time"), server.fake_time_enabled, "settings", { group: "time" }, server.fake_time_enabled ? server.fake_date : t("overview.fake_time_hint"))}
              </div>
            </section>
          </div>

          <div class="grid two">
            <section class="panel">
              <div class="panel-head">
                <h3>${t("overview.online_players")}</h3>
                ${chip(t("overview.active_count", { count: online.length }), online.length ? "good" : "subtle")}
              </div>
              ${online.length ? html`
                <div class="table-wrap">
                  <table>
                    <thead><tr><th class="w-uid">UID</th><th>${t("common.nickname")}</th><th class="w-sm">${t("common.level")}</th><th>${t("common.endpoint")}</th><th class="w-actions"></th></tr></thead>
                    <tbody>
                      ${online.map(player => html`
                        <tr>
                          <td class="mono">${player.uid}</td>
                          <td><strong>${player.nickname || t("common.no_nickname")}</strong></td>
                          <td>${player.level}</td>
                          <td class="muted mono">${player.endpoint}</td>
                          <td class="row-actions">
                            <button class="btn ghost small" type="button" data-open-account="${player.uid}" title="${t("common.open_account")}">${t("common.account")}</button>
                            <button class="btn ghost small" type="button" data-open-command="${player.uid}" title="${t("common.run_command")}">${t("common.command")}</button>
                            <button class="btn danger small" type="button" data-kick="${player.uid}">${t("common.kick")}</button>
                          </td>
                        </tr>`)}
                    </tbody>
                  </table>
                </div>` : emptyState(t("overview.no_players"))}
            </section>

            <section class="panel">
              <div class="panel-head">
                <h3>${t("overview.attention")}</h3>
                ${chip(t("overview.flagged_count", { count: attention.length }), attention.length ? "warn" : "good")}
              </div>
              ${attention.length ? html`
                <div class="table-wrap">
                  <table>
                    <thead><tr><th class="w-uid">UID</th><th>${t("common.user")}</th><th>${t("common.email")}</th><th class="w-md">${t("common.reason")}</th></tr></thead>
                    <tbody>
                      ${attention.map(account => html`
                        <tr class="clickable" data-open-account="${account.uid}">
                          <td class="mono">${account.uid}</td>
                          <td><strong>${account.username}</strong><div class="muted">${account.role}</div></td>
                          <td class="muted">${account.email}</td>
                          <td>${chip(t(`overview.reason.${account.reason}`), account.reason === "banned" ? "bad" : "warn")}</td>
                        </tr>`)}
                    </tbody>
                  </table>
                </div>` : emptyState(t("overview.no_attention"))}
            </section>
          </div>

          <section class="panel">
            <div class="panel-head"><h3>${t("overview.role_mix")}</h3></div>
            <div class="panel-body stack">
              ${roles.length ? roles.map(role => html`
                <div class="bar-row">
                  <span class="bar-row-label">${role.role}</span>
                  <div class="bar"><div class="bar-fill accent" style="width: ${Math.round(role.count / maxRole * 100)}%"></div></div>
                  <span class="bar-row-value">${fmt.number(role.count)}</span>
                </div>`) : emptyState(t("common.no_data"))}
            </div>
          </section>`);

        on(root, "[data-switch]", "change", async event => {
          const input = event.currentTarget;
          const endpoint = input.dataset.switch === "registration"
            ? "/admin/api/registration"
            : "/admin/api/registration-identity-limit";
          const enabled = input.checked;
          input.disabled = true;
          try {
            await api(endpoint, { method: "POST", body: { enabled } });
            // Refresh the cache for the other modules, but leave the DOM alone: the browser already
            // flipped this switch, and re-rendering the module would scroll the page back to the top.
            state.cache.overview = await api("/admin/api/overview");
            input.disabled = false;
            toast(t(enabled ? "common.enabled" : "common.disabled"), "good");
          } catch (error) {
            input.checked = !enabled;
            input.disabled = false;
            toast(errorMessage(error), "bad");
          }
        });
        on(root, "[data-open-account]", "click", event => navigate("accounts", { id: event.currentTarget.dataset.openAccount }));
        on(root, "[data-open-command]", "click", event => {
          event.stopPropagation();
          navigate("commands", { target: event.currentTarget.dataset.openCommand });
        });
        on(root, "[data-kick]", "click", async event => {
          event.stopPropagation();
          const uid = event.currentTarget.dataset.kick;
          if (!await confirmDialog({ title: t("sessions.kick_title", { uid }), message: t("sessions.kick_message"), danger: true, confirmLabel: t("common.kick") })) return;
          await run(event.currentTarget, async () => {
            await api(`/admin/api/sessions/${uid}/kick`, { method: "POST" });
            state.cache.overview = await api("/admin/api/overview");
            renderOverview(root);
          }, t("sessions.kicked"));
        });
        on(root, "[data-go]", "click", event => {
          const target = event.currentTarget.dataset.go;
          const params = event.currentTarget.dataset.goGroup ? { group: event.currentTarget.dataset.goGroup } : {};
          navigate(target, params);
        });
      }

      function overviewSwitch(id, label, enabled, hint) {
        return html`
          <div class="switch-row">
            <div>
              <strong>${label}</strong>
              <div class="muted small">${hint}</div>
            </div>
            <label class="switch">
              <input type="checkbox" data-switch="${id}" ${enabled ? "checked" : ""}>
              <span class="switch-track"></span>
            </label>
          </div>`;
      }

      function overviewLink(label, enabled, target, params, hint) {
        return html`
          <div class="switch-row">
            <div>
              <strong>${label}</strong>
              <div class="muted small">${hint}</div>
            </div>
            <div class="row">
              ${chip(t(enabled ? "common.on" : "common.off"), enabled ? "good" : "subtle")}
              <button class="btn ghost small" type="button" data-go="${target}" data-go-group="${params.group || ""}">${t("common.open")}</button>
            </div>
          </div>`;
      }

""";
    }
}
