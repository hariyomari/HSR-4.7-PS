namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string MaintenanceModule()
    {
        return """
      // ========================================================= maintenance
      const maintenanceState = {
        data: null,
        rateLimits: null,
        newKey: null,
        revoked: 0
      };

      registerModule({
        id: "maintenance",
        icon: ICONS.maintenance,
        poll: 10000,
        async load() {
          await loadMaintenance();
        },
        async tick() {
          await loadMaintenance();
          rerender();
        },
        render(root) {
          renderMaintenance(root);
        }
      });

      async function loadMaintenance() {
        const [data, rateLimits] = await Promise.all([
          api("/admin/api/maintenance"),
          api("/admin/api/maintenance/rate-limits").catch(() => ({ entries: [], count: 0, blocked: 0 }))
        ]);
        maintenanceState.data = data;
        maintenanceState.rateLimits = rateLimits;
      }

      function renderMaintenance(root) {
        const data = maintenanceState.data || {};
        const limits = maintenanceState.rateLimits || {};
        const entries = limits.entries || [];
        const now = limits.server_time || Math.floor(Date.now() / 1000);
        const usedMb = Math.max(0, (data.machine_total_mb || 0) - (data.machine_available_mb || 0));

        root.innerHTML = String(html`
          ${maintenanceState.newKey ? html`
            <div class="notice warn key-notice">
              <div>
                <strong>${t("maintenance.new_key_title")}</strong>
                <div class="muted small">${t("maintenance.new_key_message", { count: maintenanceState.revoked })}</div>
                <code class="key-value">${maintenanceState.newKey}</code>
              </div>
              <div class="row">
                <button class="btn primary small" type="button" id="maint-copy-key">${t("common.copy")}</button>
                <button class="btn ghost small" type="button" id="maint-dismiss-key">${t("common.dismiss")}</button>
              </div>
            </div>` : ""}

          <div class="metric-grid">
            ${metric(t("maintenance.uids"), fmt.number(data.uid_count))}
            ${metric(t("maintenance.dirty"), fmt.number(data.dirty_uids), { kind: data.dirty_uids ? "warn" : "" })}
            ${metric(t("maintenance.last_save"), data.last_save ? fmt.relative(data.last_save) : t("common.never"), { hint: data.last_save ? fmt.unix(data.last_save) : "" })}
            ${metric(t("maintenance.db_size"), data.db_size ? fmt.bytes(data.db_size) : "-", { hint: `${data.db_type || ""} · ${data.db_name || ""}` })}
            ${metric(t("overview.uptime"), fmt.duration(data.uptime_seconds))}
            ${metric(t("overview.process_memory"), fmt.mb(data.process_memory_mb), { hint: t("maintenance.heap", { value: fmt.mb(data.gc_heap_mb), gen2: data.gc_collections }) })}
            ${metric(t("overview.machine_memory"), data.machine_total_mb ? `${fmt.mb(usedMb)} / ${fmt.mb(data.machine_total_mb)}` : "-")}
            ${metric(t("maintenance.threads"), fmt.number(data.threads), { hint: t("maintenance.processors", { count: data.processors }) })}
            ${metric(t("maintenance.connections"), fmt.number(data.connections))}
            ${metric(t("maintenance.admin_sessions"), fmt.number(data.admin_sessions))}
          </div>

          <div class="grid two">
            <section class="panel">
              <div class="panel-head"><h3>${t("maintenance.actions")}</h3></div>
              <div class="panel-body stack">
                <div class="action-row">
                  <div><strong>${t("maintenance.save_now")}</strong><div class="muted small">${t("maintenance.save_now_hint")}</div></div>
                  <button class="btn primary small" type="button" id="maint-save">${t("maintenance.save_now")}</button>
                </div>
                <div class="action-row">
                  <div><strong>${t("maintenance.reload")}</strong><div class="muted small">${t("maintenance.reload_hint")}</div></div>
                  <div class="row">
                    <button class="btn ghost small" type="button" data-reload="banner">${t("maintenance.reload_banner")}</button>
                    <button class="btn ghost small" type="button" data-reload="activity">${t("maintenance.reload_activity")}</button>
                    <button class="btn ghost small" type="button" data-reload="videokey">${t("maintenance.reload_videokey")}</button>
                  </div>
                </div>
                <div class="action-row">
                  <div><strong>${t("sessions.kick_all")}</strong><div class="muted small">${t("maintenance.kick_all_hint")}</div></div>
                  <button class="btn danger small" type="button" id="maint-kick-all">${t("sessions.kick_all")}</button>
                </div>
                <div class="action-row">
                  <div><strong>${t("maintenance.rotate_key")}</strong><div class="muted small">${t("maintenance.rotate_key_hint")}</div></div>
                  <button class="btn danger small" type="button" id="maint-rotate">${t("maintenance.rotate_key")}</button>
                </div>
              </div>
            </section>

            <section class="panel">
              <div class="panel-head"><h3>${t("maintenance.environment")}</h3></div>
              <div class="panel-body kv-grid">
                ${kv(t("shell.version"), data.version)}
                ${kv(t("overview.os"), data.os)}
                ${kv(t("overview.database"), `${data.db_type || ""} (${data.db_name || ""})`)}
                ${kv(t("overview.server_time"), fmt.unix(data.server_time))}
                ${kv(t("maintenance.handbook_languages"), (data.handbook_languages || []).length ? html`<span class="chip-list">${data.handbook_languages.map(lang => html`<a class="chip clickable" href="/handbook/content?lang=${encodeURIComponent(lang)}" target="_blank" rel="noopener">${lang}</a>`)}</span>` : t("common.none"))}
              </div>
            </section>
          </div>

          <section class="panel">
            <div class="panel-head">
              <div>
                <h3>${t("maintenance.rate_limits")}</h3>
                <div class="muted small">${t("maintenance.rate_limits_hint")}</div>
              </div>
              <div class="row">
                ${chip(t("maintenance.blocked_count", { count: limits.blocked || 0 }), limits.blocked ? "bad" : "subtle")}
                <button class="btn ghost small" type="button" id="maint-clear-blocked" ${limits.blocked ? "" : "disabled"}>${t("maintenance.clear_blocked")}</button>
                <button class="btn ghost small" type="button" id="maint-clear-all" ${entries.length ? "" : "disabled"}>${t("maintenance.clear_all")}</button>
              </div>
            </div>
            ${entries.length ? html`
              <div class="table-wrap short">
                <table>
                  <thead><tr><th>${t("maintenance.key")}</th><th class="w-md">${t("maintenance.action")}</th><th class="w-sm">${t("mail.count")}</th><th class="w-lg">${t("maintenance.window")}</th><th class="w-lg">${t("maintenance.blocked_until")}</th><th class="w-actions"></th></tr></thead>
                  <tbody>
                    ${entries.map(entry => html`
                      <tr class="${entry.blocked_until > now ? "row-bad" : ""}">
                        <td class="mono muted" title="${entry.key}">${entry.key.slice(0, 16)}&hellip;</td>
                        <td>${chip(entry.action, "subtle")}</td>
                        <td>${entry.count}</td>
                        <td class="muted">${fmt.unix(entry.window_start)}</td>
                        <td>${entry.blocked_until > now ? html`<strong class="bad-text">${fmt.duration(entry.blocked_until - now)}</strong>` : html`<span class="muted">-</span>`}</td>
                        <td class="row-actions"><button class="btn ghost small" type="button" data-clear-key="${entry.key}">${t("common.clear")}</button></td>
                      </tr>`)}
                  </tbody>
                </table>
              </div>` : emptyState(t("maintenance.no_rate_limits"))}
          </section>`);

        $("#maint-copy-key")?.addEventListener("click", () => copyText(maintenanceState.newKey));
        $("#maint-dismiss-key")?.addEventListener("click", () => {
          maintenanceState.newKey = null;
          renderMaintenance(root);
        });
        $("#maint-save").addEventListener("click", event => run(event.currentTarget, async () => {
          const result = await api("/admin/api/maintenance/save", { method: "POST" });
          await loadMaintenance();
          state.cache.overview = null;
          renderMaintenance(root);
          return result;
        }, result => t("maintenance.saved", { seconds: result?.seconds ?? 0, count: result?.flushed ?? 0 })));
        on(root, "[data-reload]", "click", event => {
          const target = event.currentTarget.dataset.reload;
          run(event.currentTarget, () => api("/admin/api/maintenance/reload", { method: "POST", body: { target } }), result => result?.message || t("common.done"));
        });
        $("#maint-kick-all").addEventListener("click", async event => {
          if (!await confirmDialog({ title: t("sessions.kick_all"), message: t("sessions.kick_all_message", { count: data.connections }), danger: true, requireText: "KICK", confirmLabel: t("sessions.kick_all") })) return;
          await run(event.currentTarget, () => api("/admin/api/sessions/kick-all", { method: "POST" }), result => t("sessions.kicked_count", { count: result?.kicked ?? 0 }));
        });
        $("#maint-rotate").addEventListener("click", async event => {
          if (!await confirmDialog({ title: t("maintenance.rotate_key"), message: t("maintenance.rotate_confirm"), danger: true, requireText: "ROTATE", confirmLabel: t("maintenance.rotate_key") })) return;
          await run(event.currentTarget, async () => {
            const result = await api("/admin/api/maintenance/rotate-admin-key", { method: "POST" });
            maintenanceState.newKey = result.admin_key;
            maintenanceState.revoked = result.revoked_sessions;
            await loadMaintenance();
            renderMaintenance(root);
          }, t("maintenance.rotated"));
        });
        const clearLimits = async (button, body, message) => {
          await run(button, async () => {
            const result = await api("/admin/api/maintenance/rate-limits/clear", { method: "POST", body });
            await loadMaintenance();
            renderMaintenance(root);
            return result;
          }, result => message(result?.cleared ?? 0));
        };
        $("#maint-clear-blocked").addEventListener("click", event => clearLimits(event.currentTarget, { blocked_only: true }, count => t("maintenance.cleared", { count })));
        $("#maint-clear-all").addEventListener("click", async event => {
          if (!await confirmDialog({ title: t("maintenance.clear_all"), message: t("maintenance.clear_all_message"), danger: true })) return;
          await clearLimits(event.currentTarget, { blocked_only: false }, count => t("maintenance.cleared", { count }));
        });
        on(root, "[data-clear-key]", "click", event => clearLimits(event.currentTarget, { key: event.currentTarget.dataset.clearKey }, count => t("maintenance.cleared", { count })));
      }

""";
    }
}
