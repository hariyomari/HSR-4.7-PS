namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string WarpsModule()
    {
        return """
      // =============================================================== warps
      // Limited banner schedule (ServerOption.Warps). Lineup edits are staged in a client-side draft of the
      // Manual list; nothing reaches the server until Apply, which saves the draft and switches to Manual.
      const WARP_MODES = ["Static", "Rotation", "Manual"];
      const WARP_TYPES = {
        AvatarUp: { key: "character", character: true },
        WeaponUp: { key: "lightcone", lightcone: true },
        CollaborationAvatarUp: { key: "character", character: true, collab: true },
        CollaborationWeaponUp: { key: "lightcone", lightcone: true, collab: true },
        Normal: { key: "standard" },
        Newbie: { key: "newbie" }
      };
      const WARP_EDIT_ICON = '<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M12.5 4.5l3 3L7 16H4v-3zm-2 2l3 3"/></svg>';
      const warpsState = {
        data: null,
        pool: { phases: [], banners: [], names: {} },
        poolLineup: "",
        poolById: new Map(),
        draft: [],
        seed: "[]",
        dropped: 0,
        loadedAt: 0,
        loadPhase: "",
        restartPhase: "",
        picker: { query: "", version: "", type: "", pair: true },
        closeDialog: null,
        refreshDialog: null
      };

      registerModule({
        id: "warps",
        icon: ICONS.warps,
        async load({ force }) {
          const [data, cached] = await Promise.all([
            api("/admin/api/warps"),
            warpsState.data && !force ? warpsState.pool : api("/admin/api/warps/pool")
          ]);
          // Phase lineups follow the PhaseLineup settings, which the Settings page can change in between.
          const pool = cached === warpsState.pool && warpLineupKey(data) !== warpsState.poolLineup
            ? await api("/admin/api/warps/pool")
            : cached;
          setWarpsPool(pool, data);
          setWarpsData(data, force);
          if (!warpsBookReady()) {
            handbook.ensureDefault().then(book => {
              if (!book || state.activeModule !== "warps") return;
              refreshWarps();
              warpsState.refreshDialog?.();
            });
          }
        },
        unload() {
          warpsState.closeDialog?.();
        },
        render(root) {
          renderWarps(root);
        }
      });

      function warpsBookReady() {
        const lang = handbook.defaultLang();
        return Boolean(lang && handbook.cache[lang]);
      }

      function refreshWarps() {
        if (state.activeModule === "warps") renderWarps(els.moduleRoot);
      }

      function warpLineupKey(data) {
        return String(data?.phase_lineup);
      }

      // Collab banners are their own group (ServerOption.Warps.CollabBanners), never part of a Manual list.
      function warpIsCollab(id) {
        return Boolean(warpType(warpsState.poolById.get(id)?.gacha_type).collab);
      }

      function setWarpsPool(pool, data) {
        warpsState.pool = { phases: pool?.phases || [], banners: pool?.banners || [], names: pool?.names || {} };
        warpsState.poolById = new Map(warpsState.pool.banners.map(banner => [banner.id, banner]));
        warpsState.poolLineup = warpLineupKey(data);
      }

      // New server data. The draft is re-seeded from it unless it holds edits the caller wants to keep; the
      // phase selects jump to the rotation's current phase whenever that changes.
      function setWarpsData(data, discardDraft) {
        const keepDraft = !discardDraft && warpsDirty();
        const previousPhase = warpsState.data?.rotation?.current_phase;
        const seed = warpsSeed(data);
        warpsState.data = data;
        warpsState.loadedAt = Date.now();
        warpsState.seed = JSON.stringify(seed.entries);
        warpsState.dropped = seed.dropped;
        if (!keepDraft) warpsState.draft = seed.entries;
        const phases = warpsState.pool.phases;
        const current = data.rotation?.current_phase || "";
        const moved = current !== previousPhase;
        if (moved || !phases.some(phase => phase.key === warpsState.loadPhase)) warpsState.loadPhase = current || phases[phases.length - 1]?.key || "";
        if (moved || !phases.some(phase => phase.key === warpsState.restartPhase)) warpsState.restartPhase = current || phases[0]?.key || "";
      }

      function warpsDirty() {
        return JSON.stringify(warpsState.draft) !== warpsState.seed;
      }

      // Every draft entry is built here so the key order is stable and the dirty check can compare JSON.
      function warpEntry(id, source = {}) {
        return {
          gacha_id: Number(id),
          begin_time: Number(source.begin_time) || 0,
          end_time: Number(source.end_time) || 0,
          rate_up5: Array.isArray(source.rate_up5) ? source.rate_up5.map(Number) : null,
          rate_up4: Array.isArray(source.rate_up4) ? source.rate_up4.map(Number) : null,
          paired_gacha_id: Number(source.paired_gacha_id) || 0
        };
      }

      // Default light cone partners, as the server pairs a lineup: only when the partner is listed too.
      function warpWithDefaultPairs(entries) {
        const ids = new Set(entries.map(entry => entry.gacha_id));
        return entries.map(entry => {
          const pair = warpsState.poolById.get(entry.gacha_id)?.default_pair_id || 0;
          return { ...entry, paired_gacha_id: ids.has(pair) ? pair : 0 };
        });
      }

      // Draft seed: the saved Manual list in Manual mode, otherwise the running banners as rolling entries.
      // Banners outside the eligible history pool cannot be part of a Manual list and are left out.
      function warpsSeed(data) {
        const manual = data.mode === "Manual";
        const source = manual
          ? (data.manual || []).map(entry => warpEntry(entry.gacha_id, entry))
          : (data.scheduled || []).filter(banner => banner.running).map(banner => warpEntry(banner.id));
        const entries = [];
        let dropped = 0;
        for (const entry of source) {
          if (!warpsState.poolById.has(entry.gacha_id) || warpIsCollab(entry.gacha_id) || entries.some(item => item.gacha_id === entry.gacha_id)) {
            dropped++;
            continue;
          }
          if (entry.paired_gacha_id && (!warpsState.poolById.has(entry.paired_gacha_id) || warpIsCollab(entry.paired_gacha_id))) entry.paired_gacha_id = 0;
          entries.push(entry);
        }
        return { entries: manual ? entries : warpWithDefaultPairs(entries), dropped };
      }

      // Server clock, advanced by the time since the data was fetched.
      function warpsNow() {
        return (Number(warpsState.data?.now) || 0) + Math.floor((Date.now() - warpsState.loadedAt) / 1000);
      }

      function warpType(gachaType) {
        return WARP_TYPES[gachaType] || { key: "" };
      }

      function warpTypeChips(gachaType) {
        const type = warpType(gachaType);
        return html`${chip(type.key ? t(`warps.type.${type.key}`) : gachaType || "?", type.character ? "accent" : "")}${type.collab ? chip(t("warps.type.collab"), "warn") : ""}`;
      }

      // Name of a featured item: Config/Custom/WarpItemNames.json in the panel language, then in the other one,
      // then the handbook (characters live in the Avatar section, light cones in Item).
      function warpItemName(id) {
        const numeric = Number(id);
        const names = warpsState.pool.names || {};
        const [first, second] = String(state.lang || "").startsWith("zh") ? ["zh-CN", "en"] : ["en", "zh-CN"];
        const listed = names[first]?.[numeric] || names[second]?.[numeric];
        if (listed) return listed;
        const name = (numeric < 10000 ? handbook.name("Avatar", numeric) : "") || handbook.name("Item", numeric);
        return name && !/^\[-?\d+\]$/.test(name) ? name : "";
      }

      // Every name the picker search matches on, lower-cased: both languages, whatever the panel shows.
      function warpItemSearchText(id) {
        const names = warpsState.pool.names || {};
        return [warpItemName(id), names.en?.[id], names["zh-CN"]?.[id]].filter(Boolean).join("\n").toLowerCase();
      }

      function warpItemLabel(id) {
        return warpItemName(id) || `#${id}`;
      }

      function warpNames(ids) {
        return (ids || []).map(warpItemLabel).join(" · ");
      }

      function warpDate(seconds) {
        const value = Number(seconds) || 0;
        return value > 0 ? new Date(value * 1000).toLocaleDateString(state.lang) : t("common.none");
      }

      // With the Version lineup a phase stands for its whole version (the pool lists one per version).
      function warpPhaseLabel(phase) {
        const name = warpsState.data?.phase_lineup === "Version" ? phase.version : phase.key;
        return `${name} · ${warpDate(phase.start_time)}`;
      }

      // Phase keys are "<version>-<phase>"; a configured start phase need not be the version's first.
      function warpPhaseName(key) {
        return key && warpsState.data?.phase_lineup === "Version" ? String(key).replace(/-\d+$/, "") : key;
      }

      function warpBannerLabel(banner) {
        return `${banner.id} · ${banner.version}-${banner.phase} · ${warpNames(banner.rate_up5)}`;
      }

      function warpParseIds(text) {
        const ids = [];
        const invalid = [];
        for (const token of String(text || "").split(/[\s,;，]+/).filter(Boolean)) {
          const value = Number(token);
          if (!Number.isInteger(value) || value <= 0) invalid.push(token);
          else if (!ids.includes(value)) ids.push(value);
        }
        return { ids, invalid };
      }

      // Cards: the server's scheduled banners, or the draft once it differs from them.
      function warpsLineup() {
        const data = warpsState.data || {};
        if (warpsDirty()) {
          const rotation = data.rotation || {};
          const now = warpsNow();
          return warpsState.draft.map(entry => {
            const banner = warpsState.poolById.get(entry.gacha_id) || {};
            const begin = entry.begin_time > 0 ? entry.begin_time : Number(rotation.period_begin) || 0;
            const end = entry.end_time > 0 ? entry.end_time : Number(rotation.period_end) || 0;
            return {
              id: entry.gacha_id,
              gacha_type: banner.gacha_type,
              version: banner.version,
              phase: banner.phase,
              rate_up5: entry.rate_up5 ?? banner.rate_up5 ?? [],
              rate_up4: entry.rate_up4 ?? banner.rate_up4 ?? [],
              begin,
              end,
              running: begin <= now && now <= end,
              rolling: !(entry.end_time > 0),
              custom: Boolean(entry.rate_up5 || entry.rate_up4),
              pair: entry.paired_gacha_id,
              editable: true
            };
          });
        }
        const manual = data.mode === "Manual";
        const entries = new Map(warpsState.draft.map(entry => [entry.gacha_id, entry]));
        return (data.scheduled || []).map(banner => {
          const entry = entries.get(banner.id) || null;
          return {
            id: banner.id,
            gacha_type: banner.gacha_type,
            version: banner.version,
            phase: banner.phase,
            rate_up5: banner.rate_up5 || [],
            rate_up4: banner.rate_up4 || [],
            begin: banner.begin_time,
            end: banner.end_time,
            running: banner.running,
            rolling: data.mode === "Rotation" || (manual && Boolean(entry) && !(entry.end_time > 0)),
            custom: manual && Boolean(entry && (entry.rate_up5 || entry.rate_up4)),
            pair: entry ? entry.paired_gacha_id : banner.default_pair_id || 0,
            editable: Boolean(entry)
          };
        });
      }

      function renderWarps(root) {
        const data = warpsState.data || {};
        const rotation = data.rotation || {};
        const mode = WARP_MODES.includes(data.mode) ? data.mode : "Static";
        const phases = warpsState.pool.phases;
        const poolEmpty = !(Number(data.pool_size) > 0);
        const dirty = warpsDirty();
        const lineup = warpsLineup();
        const listed = new Set(lineup.map(view => view.id));
        const ineligible = data.ineligible || [];
        const permanent = data.permanent || [];
        const collab = data.collab || [];
        const collabOn = Boolean(data.collab_banners);
        const phaseLineup = ["Overlap", "Version"].includes(data.phase_lineup) ? data.phase_lineup : "Started";

        setModuleActions(dirty ? chip(t("warps.unsaved"), "warn") : "");
        root.innerHTML = String(html`
          ${poolEmpty ? html`<div class="notice warn">${t("warps.pool_empty")}</div>` : ""}
          <section class="panel">
            <div class="panel-head">
              <div>
                <h3>${t("warps.schedule")}</h3>
                <div class="muted small">${t(`warps.mode_hint.${mode}`)}</div>
              </div>
              <div class="segmented" role="group" aria-label="${t("warps.mode")}">
                ${WARP_MODES.map(item => html`<button type="button" class="${item === mode ? "active" : ""}" data-warp-mode="${item}">${t(`warps.mode.${item}`)}</button>`)}
              </div>
            </div>
            <div class="panel-body stack">
              <div>
                ${toggleSwitch("warps-story", Boolean(data.story), t("warps.story"))}
                <div class="muted small">${t("warps.story_hint")}</div>
              </div>
              ${data.story && mode === "Manual" ? html`<div class="notice">${t("warps.story_manual")}</div>` : ""}
              ${data.story_active ? html`<div class="notice">${t("warps.story_active")}</div>` : ""}
            </div>
            <div class="panel-body kv-grid">
              ${kv(t("warps.period_length"), t("warps.days", { count: rotation.days ?? "?" }))}
              ${kv(t("warps.start_date"), rotation.start || t("warps.start_unset"))}
              ${kv(t("warps.start_phase"), warpPhaseName(rotation.start_phase) || t("warps.start_phase_oldest"))}
              ${kv(t("warps.current_phase"), warpPhaseName(rotation.current_phase) || t("common.none"))}
              ${kv(t("warps.next_phase"), warpPhaseName(rotation.next_phase) || t("common.none"))}
              ${kv(t("warps.period"), `${warpDate(rotation.period_begin)} – ${warpDate(rotation.period_end)}`)}
              ${kv(t("warps.period_ends"), rotation.period_end ? fmt.relative(Number(rotation.period_end) + 1) : t("common.none"))}
              ${kv(t("warps.phase_lineup"), t(`warps.lineup.${phaseLineup}`))}
            </div>
            <div class="panel-body row space-between">
              <div class="row">
                <label class="muted small" for="warps-restart-phase">${t("warps.restart_label")}</label>
                <select id="warps-restart-phase" class="inline-select" ${phases.length ? "" : "disabled"}>
                  ${phases.map(phase => option(phase.key, warpPhaseLabel(phase), warpsState.restartPhase))}
                </select>
                <button class="btn ghost small" type="button" id="warps-restart" ${phases.length ? "" : "disabled"}>${t("warps.restart")}</button>
              </div>
              <button class="link-button" type="button" id="warps-open-settings">${t("warps.settings_link")}</button>
            </div>
          </section>

          <section class="panel">
            <div class="panel-head">
              <div class="row">
                <h3>${t("warps.active")}</h3>
                ${chip(t("warps.banner_count", { count: lineup.length }), "subtle")}
                ${dirty ? chip(t("warps.draft"), "warn") : ""}
              </div>
              <div class="row">
                <select id="warps-load-phase" class="inline-select" aria-label="${t("warps.load_phase")}" ${phases.length ? "" : "disabled"}>
                  ${phases.map(phase => option(phase.key, warpPhaseLabel(phase), warpsState.loadPhase))}
                </select>
                <button class="btn ghost small" type="button" id="warps-load" ${phases.length ? "" : "disabled"}>${t("warps.load_phase")}</button>
                ${mode === "Manual" ? html`<button class="btn ghost small" type="button" id="warps-back">${t("warps.back_to_rotation")}</button>` : ""}
              </div>
            </div>
            ${dirty ? html`
              <div class="panel-body">
                <div class="notice warn warp-draft-notice">
                  <span>${t(mode === "Manual" ? "warps.draft_notice" : "warps.draft_notice_switch")}</span>
                  <span class="row">
                    <button class="btn ghost small" type="button" id="warps-discard">${t("settings.discard")}</button>
                    <button class="btn primary small" type="button" id="warps-apply">${t("common.apply")}</button>
                  </span>
                </div>
              </div>` : ""}
            <div class="panel-body stack">
              <div class="warp-grid">
                ${lineup.map(view => warpCard(view, listed))}
                ${poolEmpty ? "" : html`
                  <button type="button" class="warp-card warp-add" id="warps-add">
                    <span class="warp-add-plus" aria-hidden="true">+</span>
                    <span>${t("warps.add")}</span>
                  </button>`}
              </div>
              ${lineup.length ? "" : html`<p class="muted small">${t("warps.lineup_empty")}</p>`}
              ${warpsState.dropped ? html`<p class="muted small">${t(mode === "Manual" ? "warps.dropped_manual" : "warps.dropped_other", { count: warpsState.dropped })}</p>` : ""}
              ${ineligible.length ? html`
                <details class="muted small">
                  <summary>${t("warps.ineligible", { count: ineligible.length })}</summary>
                  <p class="mono">${ineligible.join(", ")}</p>
                </details>` : ""}
            </div>
          </section>

          <section class="panel">
            <div class="panel-head">
              <h3>${t("warps.permanent")}</h3>
              <span class="muted small">${t("warps.permanent_hint")}</span>
            </div>
            <div class="panel-body">
              ${permanent.length
                ? html`<div class="warp-grid compact">${permanent.map(banner => warpPermanentCard(banner))}</div>`
                : html`<p class="muted small">${t("warps.permanent_empty")}</p>`}
            </div>
          </section>

          <section class="panel">
            <div class="panel-head">
              <div>
                <h3>${t("warps.collab")}</h3>
                <div class="muted small">${t("warps.collab_hint")}</div>
              </div>
              ${toggleSwitch("warps-collab", collabOn, t("warps.collab_switch"))}
            </div>
            <div class="panel-body stack">
              ${collabOn ? "" : html`<div class="notice">${t("warps.collab_off_note")}</div>`}
              ${collab.length
                ? html`<div class="warp-grid compact">${collab.map(banner => warpPermanentCard(banner, !collabOn))}</div>`
                : html`<p class="muted small">${t("warps.collab_empty")}</p>`}
            </div>
          </section>`);

        on(root, "[data-warp-mode]", "click", event => switchWarpMode(event.currentTarget, event.currentTarget.dataset.warpMode));
        $("#warps-story").addEventListener("change", event => setWarpSwitch(event.currentTarget, "Warps.Story", "warps.story_on", "warps.story_off"));
        $("#warps-collab").addEventListener("change", event => setWarpSwitch(event.currentTarget, "Warps.CollabBanners", "warps.collab_on", "warps.collab_off"));
        $("#warps-back")?.addEventListener("click", event => switchWarpMode(event.currentTarget, "Rotation"));
        $("#warps-restart-phase").addEventListener("change", event => warpsState.restartPhase = event.currentTarget.value);
        $("#warps-restart").addEventListener("click", event => restartWarpRotation(event.currentTarget));
        $("#warps-open-settings").addEventListener("click", () => navigate("settings", { group: "warps" }));
        $("#warps-load-phase").addEventListener("change", event => warpsState.loadPhase = event.currentTarget.value);
        $("#warps-load").addEventListener("click", () => loadWarpPhase());
        $("#warps-discard")?.addEventListener("click", () => {
          warpsState.draft = JSON.parse(warpsState.seed);
          refreshWarps();
        });
        $("#warps-apply")?.addEventListener("click", event => applyWarpDraft(event.currentTarget));
        $("#warps-add")?.addEventListener("click", () => openWarpPicker());
        on(root, "[data-warp-edit]", "click", event => openWarpEditor(Number(event.currentTarget.dataset.warpEdit)));
        on(root, "[data-warp-remove]", "click", event => removeWarpEntry(Number(event.currentTarget.dataset.warpRemove)));
      }

      function warpCard(view, listed) {
        const type = warpType(view.gacha_type);
        const now = warpsNow();
        const timeKey = view.running ? (view.rolling ? "warps.rolls" : "warps.ends") : (view.begin > now ? "warps.starts" : "warps.ended");
        const timeValue = timeKey === "warps.starts" ? view.begin : view.end;
        const pairBanner = view.pair ? warpsState.poolById.get(view.pair) : null;
        const blocked = view.editable ? "" : t("warps.not_editable");
        return html`
          <article class="warp-card ${view.running ? "" : "idle"}">
            <div class="warp-card-head">
              <span class="chip-list">
                ${warpTypeChips(view.gacha_type)}
                ${view.version ? chip(`${view.version}-${view.phase}`, "subtle") : ""}
                ${view.running ? "" : chip(t("warps.not_running"), "bad")}
                ${view.custom ? chip(t("warps.custom"), "accent") : ""}
              </span>
              <span class="warp-card-actions">
                <button class="btn ghost small" type="button" data-warp-edit="${view.id}" title="${blocked || t("warps.edit")}" aria-label="${t("warps.edit")}" ${view.editable ? "" : "disabled"}>${raw(WARP_EDIT_ICON)}</button>
                <button class="btn ghost small" type="button" data-warp-remove="${view.id}" title="${blocked || t("common.remove")}" aria-label="${t("common.remove")}" ${view.editable ? "" : "disabled"}>&times;</button>
              </span>
            </div>
            <div class="warp-featured">
              <strong title="${view.rate_up5.join(", ")}"><span class="warp-star">5★</span> ${warpNames(view.rate_up5)}</strong>
              ${view.rate_up4.length ? html`<span class="muted small" title="${view.rate_up4.join(", ")}"><span class="warp-star">4★</span> ${warpNames(view.rate_up4)}</span>` : ""}
            </div>
            ${type.character ? html`
              <div class="small">
                ${view.pair
                  ? html`${t("warps.paired", { name: pairBanner ? warpNames(pairBanner.rate_up5) : `#${view.pair}` })} <span class="mono muted">#${view.pair}</span> ${listed.has(view.pair) ? "" : chip(t("warps.pair_missing"), "subtle")}`
                  : html`<span class="muted">${t("warps.no_pair")}</span>`}
              </div>` : ""}
            <div class="muted small"><span class="mono">#${view.id}</span> · ${t(timeKey, { date: fmt.unix(timeValue), relative: fmt.relative(timeValue) })}</div>
          </article>`;
      }

      // Permanent and collab cards; `off`: the group is switched off, so the banner is not served at all.
      function warpPermanentCard(banner, off = false) {
        return html`
          <article class="warp-card ${banner.running ? "" : "idle"}">
            <span class="chip-list">
              ${warpTypeChips(banner.gacha_type)}
              <span class="mono muted small">#${banner.id}</span>
              ${off ? chip(t("warps.not_served"), "bad") : banner.running ? "" : chip(t("warps.not_running"), "bad")}
            </span>
            <span class="small" title="${(banner.rate_up5 || []).join(", ")}"><span class="warp-star">5★</span> ${warpNames(banner.rate_up5)}</span>
          </article>`;
      }

      // ------------------------------------------------------------ actions
      // A write changed ServerOption.Warps: re-seed the draft from the response and let Settings re-read its
      // copy of the Warps.* fields, unless it holds unsaved edits of its own.
      function onWarpsSaved(data) {
        setWarpsData(data, true);
        if (!Object.keys(settingsState.edits).length) settingsState.fields = [];
        refreshWarps();
      }

      async function switchWarpMode(button, mode) {
        if (!WARP_MODES.includes(mode) || mode === warpsState.data?.mode) return;
        const label = t(`warps.mode.${mode}`);
        const message = [t(`warps.mode_confirm.${mode}`), warpsDirty() ? t("warps.discard_note") : ""].filter(Boolean).join("\n\n");
        if (!await confirmDialog({ title: t("warps.mode_title", { mode: label }), message, confirmLabel: t("warps.switch") })) return;
        await run(button, async () => {
          onWarpsSaved(await api("/admin/api/warps/mode", { method: "POST", body: { mode } }));
        }, t("warps.mode_changed", { mode: label }));
      }

      // Warps.Story and Warps.CollabBanners go through the Settings API like any other key. Neither changes the
      // server-wide limited lineup, so an unsaved draft is kept.
      async function setWarpSwitch(input, key, onMessage, offMessage) {
        const enabled = input.checked;
        const data = await run(input, async () => {
          const saved = await api("/admin/api/settings", { method: "PUT", body: { values: { [key]: enabled ? "true" : "false" } } });
          if (saved.errors?.length) throw new Error(saved.errors.join("\n"));
          return api("/admin/api/warps");
        }, t(enabled ? onMessage : offMessage));
        if (!data) {
          input.checked = !enabled;
          return;
        }
        setWarpsData(data, false);
        if (!Object.keys(settingsState.edits).length) settingsState.fields = [];
        refreshWarps();
      }

      async function restartWarpRotation(button) {
        const phase = warpsState.restartPhase;
        if (!phase) return;
        const message = [t("warps.restart_message", { phase }), warpsDirty() ? t("warps.discard_note") : ""].filter(Boolean).join("\n\n");
        if (!await confirmDialog({ title: t("warps.restart_title", { phase }), message, confirmLabel: t("warps.restart") })) return;
        await run(button, async () => {
          onWarpsSaved(await api("/admin/api/warps/rotation", { method: "POST", body: { phase } }));
        }, t("warps.restarted", { phase }));
      }

      async function loadWarpPhase() {
        const key = warpsState.loadPhase;
        const phase = warpsState.pool.phases.find(item => item.key === key);
        if (!phase) return;
        if (warpsDirty() && !await confirmDialog({ title: t("warps.load_title", { phase: key }), message: t("warps.load_message"), confirmLabel: t("warps.load_phase") })) return;
        const ids = (phase.banner_ids || []).filter(id => warpsState.poolById.has(id));
        warpsState.draft = warpWithDefaultPairs(ids.map(id => warpEntry(id)));
        refreshWarps();
        toast(t("warps.phase_loaded", { phase: key, count: ids.length }), "good");
      }

      async function applyWarpDraft(button) {
        const entries = warpsState.draft;
        const notes = [];
        if (warpsState.data?.mode !== "Manual") notes.push(t("warps.apply_switches"));
        if (!entries.length) notes.push(t("warps.apply_empty"));
        if (notes.length && !await confirmDialog({ title: t("warps.apply_title"), message: notes.join("\n\n"), confirmLabel: t("common.apply") })) return;
        await run(button, async () => {
          onWarpsSaved(await api("/admin/api/warps/manual", { method: "PUT", body: { entries } }));
        }, t("warps.applied", { count: entries.length }));
      }

      async function removeWarpEntry(id) {
        const entry = warpsState.draft.find(item => item.gacha_id === id);
        if (!entry) return;
        const pair = entry.paired_gacha_id;
        let removePair = false;
        if (pair && warpsState.draft.some(item => item.gacha_id === pair)) {
          const choice = await warpsChoice(
            t("warps.remove_title", { id }),
            t("warps.remove_message", { pair, name: warpNames(warpsState.poolById.get(pair)?.rate_up5) }),
            [{ value: "one", label: t("warps.remove_one") }, { value: "both", label: t("warps.remove_both"), kind: "danger" }]);
          if (!choice) return;
          removePair = choice === "both";
        }
        const removed = new Set(removePair ? [id, pair] : [id]);
        warpsState.draft = warpsState.draft
          .filter(item => !removed.has(item.gacha_id))
          .map(item => removed.has(item.paired_gacha_id) ? { ...item, paired_gacha_id: 0 } : item);
        refreshWarps();
      }

      function addWarpBanner(id) {
        const banner = warpsState.poolById.get(id);
        if (!banner || warpsState.draft.some(item => item.gacha_id === id)) return;
        const entry = warpEntry(id);
        const entries = [...warpsState.draft, entry];
        const added = [id];
        const pair = banner.default_pair_id || 0;
        if (warpsState.picker.pair && warpType(banner.gacha_type).character && pair && warpsState.poolById.has(pair)) {
          entry.paired_gacha_id = pair;
          if (!entries.some(item => item.gacha_id === pair)) {
            entries.push(warpEntry(pair));
            added.push(pair);
          }
        }
        warpsState.draft = entries;
        refreshWarps();
        toast(t("warps.added", { banners: added.map(item => `${item} (${warpNames(warpsState.poolById.get(item)?.rate_up5)})`).join(", ") }), "good");
      }

      // The pair select swaps the light cone banner itself: the old partner's entry turns into the new one and
      // keeps its window, unless another character still uses it or the new one is listed already.
      function warpSwapPair(entries, owner, oldPair, newPair) {
        if (!newPair) return entries;
        const shared = entries.some(item => item.gacha_id !== owner.gacha_id && item.paired_gacha_id === oldPair);
        const oldIndex = oldPair && !shared ? entries.findIndex(item => item.gacha_id === oldPair) : -1;
        if (entries.some(item => item.gacha_id === newPair)) {
          return oldIndex >= 0 ? entries.filter((_, index) => index !== oldIndex) : entries;
        }
        if (oldIndex >= 0) {
          const old = entries[oldIndex];
          return entries.map((item, index) => index === oldIndex ? warpEntry(newPair, { begin_time: old.begin_time, end_time: old.end_time }) : item);
        }
        const copy = entries.slice();
        copy.splice(copy.indexOf(owner) + 1, 0, warpEntry(newPair, { begin_time: owner.begin_time, end_time: owner.end_time }));
        return copy;
      }

      // ------------------------------------------------------------- dialogs
      // Same chrome as confirmDialog; the caller renders the content and binds its own controls.
      function warpsDialog(content, options = {}) {
        warpsState.closeDialog?.();
        const host = $("#modal-host");
        host.innerHTML = String(html`
          <div class="modal-backdrop">
            <div class="modal ${options.wide ? "wide" : ""}" role="dialog" aria-modal="true">${content}</div>
          </div>`);
        const backdrop = $(".modal-backdrop", host);
        let pressedOutside = false;
        const onKey = event => {
          if (event.key === "Escape") close();
        };
        const close = () => {
          if (warpsState.closeDialog !== close) return;
          warpsState.closeDialog = null;
          warpsState.refreshDialog = null;
          document.removeEventListener("keydown", onKey);
          host.innerHTML = "";
          options.onClose?.();
        };
        document.addEventListener("keydown", onKey);
        // Only a click that starts and ends on the backdrop closes it; selecting text in an input must not.
        backdrop.addEventListener("mousedown", event => pressedOutside = event.target === event.currentTarget);
        backdrop.addEventListener("click", event => {
          if (pressedOutside && event.target === event.currentTarget) close();
        });
        warpsState.closeDialog = close;
        return { root: $(".modal", host), close };
      }

      function warpsChoice(title, message, choices) {
        return new Promise(resolve => {
          let picked = null;
          const dialog = warpsDialog(html`
            <h3>${title}</h3>
            <p class="modal-message">${message}</p>
            <div class="modal-actions">
              <button class="btn ghost" type="button" data-warp-choice="">${t("common.cancel")}</button>
              ${choices.map(choice => html`<button class="btn ${choice.kind || ""}" type="button" data-warp-choice="${choice.value}">${choice.label}</button>`)}
            </div>`, { onClose: () => resolve(picked) });
          on(dialog.root, "[data-warp-choice]", "click", event => {
            picked = event.currentTarget.dataset.warpChoice || null;
            dialog.close();
          });
          // Default focus on the first (non-destructive) choice, not on the last button.
          $$("[data-warp-choice]", dialog.root)[1]?.focus();
        });
      }

      function warpPickerResults() {
        const { query, version, type } = warpsState.picker;
        const needle = query.trim().toLowerCase();
        return warpsState.pool.banners
          .filter(banner => {
            const info = warpType(banner.gacha_type);
            if (version && banner.version !== version) return false;
            if (info.collab) return false;
            if (type && info.key !== type) return false;
            if (!needle) return true;
            if (String(banner.id).startsWith(needle) || `${banner.version}-${banner.phase}`.startsWith(needle)) return true;
            return [...(banner.rate_up5 || []), ...(banner.rate_up4 || [])]
              .some(id => String(id) === needle || warpItemSearchText(id).includes(needle));
          })
          .sort((a, b) => b.begin_time - a.begin_time || a.id - b.id);
      }

      function openWarpPicker() {
        const picker = warpsState.picker;
        const versions = [...new Set(warpsState.pool.phases.map(phase => phase.version))].reverse();
        const types = [["", t("warps.all_types")], ["character", t("warps.type.character")], ["lightcone", t("warps.type.lightcone")]];
        const dialog = warpsDialog(html`
          <h3>${t("warps.picker_title")}</h3>
          <div class="warp-pick-tools">
            <input id="warp-pick-query" type="search" autocomplete="off" placeholder="${t("warps.picker_search")}" aria-label="${t("warps.picker_search")}" value="${picker.query}">
            <select id="warp-pick-version" aria-label="${t("warps.version")}">
              ${option("", t("warps.all_versions"), picker.version)}
              ${versions.map(version => option(version, version, picker.version))}
            </select>
            <select id="warp-pick-type" aria-label="${t("warps.type")}">
              ${types.map(([value, label]) => option(value, label, picker.type))}
            </select>
          </div>
          <div>${toggleSwitch("warp-pick-pair", picker.pair, t("warps.picker_pair"))}</div>
          <div id="warp-pick-list" class="warp-pick-list"></div>
          <div class="row space-between">
            <span class="muted small" id="warp-pick-count"></span>
            <button class="btn primary" type="button" id="warp-pick-done">${t("warps.done")}</button>
          </div>`, { wide: true });
        const root = dialog.root;
        const renderList = () => {
          const listed = new Set(warpsState.draft.map(entry => entry.gacha_id));
          const results = warpPickerResults();
          const list = $("#warp-pick-list", root);
          $("#warp-pick-count", root).textContent = t("warps.picker_count", { count: results.length });
          list.innerHTML = String(results.length ? html`${results.map(banner => html`
            <button type="button" class="warp-pick-item" data-warp-pick="${banner.id}" ${listed.has(banner.id) ? "disabled" : ""}>
              <span class="mono">${banner.id}</span>
              <span class="warp-pick-main">
                <span class="chip-list">
                  ${warpTypeChips(banner.gacha_type)}
                  ${chip(`${banner.version}-${banner.phase}`, "subtle")}
                  <strong>${warpNames(banner.rate_up5)}</strong>
                </span>
                <span class="muted small">${banner.rate_up4?.length ? `4★ ${warpNames(banner.rate_up4)} · ` : ""}${warpDate(banner.begin_time)} – ${warpDate(banner.end_time)}</span>
              </span>
              ${listed.has(banner.id) ? chip(t("warps.picker_added"), "good") : ""}
            </button>`)}` : emptyState(t("common.no_results")));
          on(list, "[data-warp-pick]", "click", event => {
            addWarpBanner(Number(event.currentTarget.dataset.warpPick));
            renderList();
          });
        };
        warpsState.refreshDialog = renderList;
        $("#warp-pick-query", root).addEventListener("input", debounce(event => {
          picker.query = event.target.value;
          renderList();
        }, 120));
        $("#warp-pick-version", root).addEventListener("change", event => {
          picker.version = event.currentTarget.value;
          renderList();
        });
        $("#warp-pick-type", root).addEventListener("change", event => {
          picker.type = event.currentTarget.value;
          renderList();
        });
        $("#warp-pick-pair", root).addEventListener("change", event => picker.pair = event.currentTarget.checked);
        $("#warp-pick-done", root).addEventListener("click", () => dialog.close());
        renderList();
        $("#warp-pick-query", root).focus();
      }

      function openWarpEditor(id) {
        const entry = warpsState.draft.find(item => item.gacha_id === id);
        const banner = warpsState.poolById.get(id);
        if (!entry || !banner) return;
        const type = warpType(banner.gacha_type);
        const history5 = banner.rate_up5 || [];
        const history4 = banner.rate_up4 || [];
        const endValue = entry.end_time > 0 ? unixToDateTimeLocal(entry.end_time) : "";
        const lightCones = type.character
          ? warpsState.pool.banners.filter(item => { const info = warpType(item.gacha_type); return info.lightcone && !info.collab; }).sort((a, b) => b.begin_time - a.begin_time || a.id - b.id)
          : [];
        const dialog = warpsDialog(html`
          <h3>${t("warps.edit_title", { id })}</h3>
          <span class="chip-list">${warpTypeChips(banner.gacha_type)}${chip(`${banner.version}-${banner.phase}`, "subtle")}</span>
          ${["5", "4"].map(rank => html`
            <div class="field">
              <span>${t(`warps.featured${rank}`)}</span>
              <div class="input-row">
                <input id="warp-edit-rate${rank}" class="mono" autocomplete="off" spellcheck="false" aria-label="${t(`warps.featured${rank}`)}" value="${((rank === "5" ? entry.rate_up5 : entry.rate_up4) ?? (rank === "5" ? history5 : history4)).join(", ")}">
                <button class="btn ghost small" type="button" data-warp-reset="${rank}">${t("warps.reset_history")}</button>
              </div>
              <div class="muted small" data-warp-names="${rank}"></div>
            </div>`)}
          <div class="field">
            <span>${t("warps.end_time")}</span>
            <input id="warp-edit-end" type="datetime-local" aria-label="${t("warps.end_time")}" value="${endValue}">
            <div class="muted small">${t("warps.end_hint", { date: fmt.unix(warpsState.data?.rotation?.period_end) })}</div>
          </div>
          ${type.character ? html`
            <div class="field">
              <span>${t("warps.paired_lc")}</span>
              <select id="warp-edit-pair" aria-label="${t("warps.paired_lc")}">
                ${option(0, t("warps.pair_none"), entry.paired_gacha_id)}
                ${lightCones.map(item => option(item.id, warpBannerLabel(item), entry.paired_gacha_id))}
              </select>
              <div class="muted small">${t("warps.pair_hint")}</div>
            </div>` : ""}
          <div class="field-error" id="warp-edit-error" role="alert"></div>
          <div class="modal-actions">
            <button class="btn ghost" type="button" id="warp-edit-cancel">${t("common.cancel")}</button>
            <button class="btn primary" type="button" id="warp-edit-save">${t("warps.edit_save")}</button>
          </div>`, { wide: true });
        const root = dialog.root;
        const showNames = rank => {
          const parsed = warpParseIds($(`#warp-edit-rate${rank}`, root).value);
          const names = parsed.ids.map(item => warpItemName(item) ? `${warpItemName(item)} (${item})` : String(item)).join(" · ");
          const invalid = parsed.invalid.length ? t("warps.error_ids", { value: parsed.invalid.join(", ") }) : "";
          $(`[data-warp-names="${rank}"]`, root).textContent = [names, invalid].filter(Boolean).join(" — ") || t("common.none");
        };
        const save = () => {
          const fail = message => {
            $("#warp-edit-error", root).textContent = message;
          };
          const rate5 = warpParseIds($("#warp-edit-rate5", root).value);
          const rate4 = warpParseIds($("#warp-edit-rate4", root).value);
          const invalid = [...rate5.invalid, ...rate4.invalid];
          if (invalid.length) return fail(t("warps.error_ids", { value: invalid.join(", ") }));
          if (!rate5.ids.length) return fail(t("warps.error_rate5"));
          const endText = $("#warp-edit-end", root).value;
          // An untouched field keeps the stored second, which datetime-local cannot show.
          const end = !endText ? 0 : endText === endValue ? entry.end_time : dateTimeLocalToUnix(endText) || 0;
          if (end > 0 && entry.begin_time > 0 && end <= entry.begin_time) return fail(t("warps.error_end", { date: fmt.unix(entry.begin_time) }));
          const same = (a, b) => a.length === b.length && a.every((value, index) => value === b[index]);
          const updated = {
            ...entry,
            end_time: end,
            rate_up5: same(rate5.ids, history5) ? null : rate5.ids,
            rate_up4: same(rate4.ids, history4) ? null : rate4.ids
          };
          let entries = warpsState.draft.map(item => item.gacha_id === id ? updated : item);
          if (type.character) {
            const pair = Number($("#warp-edit-pair", root).value) || 0;
            if (pair !== entry.paired_gacha_id) {
              updated.paired_gacha_id = pair;
              entries = warpSwapPair(entries, updated, entry.paired_gacha_id, pair);
            }
          }
          warpsState.draft = entries;
          dialog.close();
          refreshWarps();
        };
        warpsState.refreshDialog = () => {
          showNames("5");
          showNames("4");
        };
        warpsState.refreshDialog();
        on(root, "[data-warp-reset]", "click", event => {
          const rank = event.currentTarget.dataset.warpReset;
          $(`#warp-edit-rate${rank}`, root).value = (rank === "5" ? history5 : history4).join(", ");
          showNames(rank);
        });
        for (const rank of ["5", "4"]) {
          const input = $(`#warp-edit-rate${rank}`, root);
          input.addEventListener("input", () => showNames(rank));
          input.addEventListener("keydown", event => {
            if (event.key === "Enter") save();
          });
        }
        $("#warp-edit-cancel", root).addEventListener("click", () => dialog.close());
        $("#warp-edit-save", root).addEventListener("click", save);
        $("#warp-edit-rate5", root).focus();
      }

""";
    }
}
