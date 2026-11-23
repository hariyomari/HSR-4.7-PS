namespace March7thHoney.WebServer.Pages;

public static partial class AdminPageScripts
{
    private static string SettingsModule()
    {
        return """
      // ============================================================ settings
      const settingsState = {
        fields: [],
        group: "server",
        edits: {},
        errors: {},
        restartRequired: false
      };

      registerModule({
        id: "settings",
        icon: ICONS.settings,
        async load({ force, params }) {
          if (!settingsState.fields.length || force) {
            const data = await api("/admin/api/settings");
            settingsState.fields = data.fields || [];
            settingsState.edits = {};
            settingsState.errors = {};
          }
          if (params.group) settingsState.group = params.group;
        },
        render(root) {
          renderSettings(root);
        }
      });

      function settingsGroups() {
        const groups = [];
        for (const field of settingsState.fields) {
          if (!groups.includes(field.group)) groups.push(field.group);
        }
        return groups;
      }

      function hasTranslation(key) {
        return (I18N[state.lang] && key in I18N[state.lang]) || (I18N.en && key in I18N.en);
      }

      function settingLabel(field) {
        const key = `settings.field.${field.key}`;
        return hasTranslation(key) ? t(key) : field.key.split(".").pop();
      }

      function settingHint(field) {
        const key = `settings.hint.${field.key}`;
        return hasTranslation(key) ? t(key) : "";
      }

      function renderSettings(root) {
        const groups = settingsGroups();
        if (!groups.includes(settingsState.group)) settingsState.group = groups[0] || "server";
        const fields = settingsState.fields.filter(field => field.group === settingsState.group);
        const editCount = Object.keys(settingsState.edits).length;
        const groupEdits = group => settingsState.fields.filter(field => field.group === group && field.key in settingsState.edits).length;

        setModuleActions(html`
          ${editCount ? chip(t("settings.pending", { count: editCount }), "warn") : ""}
          <button class="btn ghost small" type="button" id="settings-discard" ${editCount ? "" : "disabled"}>${t("settings.discard")}</button>
          <button class="btn primary small" type="button" id="settings-save" ${editCount ? "" : "disabled"}>${t("settings.save_all")}</button>`);

        root.innerHTML = String(html`
          ${settingsState.restartRequired ? html`<div class="notice warn">${t("settings.restart_notice")}</div>` : ""}
          <div class="settings-layout">
            <nav class="settings-nav" aria-label="${t("nav.settings")}">
              ${groups.map(group => html`
                <button type="button" class="settings-nav-item ${group === settingsState.group ? "active" : ""}" data-settings-group="${group}">
                  <span>${t(`settings.group.${group}`)}</span>
                  ${groupEdits(group) ? html`<span class="dot"></span>` : ""}
                </button>`)}
            </nav>
            <section class="panel">
              <div class="panel-head">
                <div>
                  <h3>${t(`settings.group.${settingsState.group}`)}</h3>
                  ${hasTranslation(`settings.group.${settingsState.group}.hint`) ? html`<div class="muted small">${t(`settings.group.${settingsState.group}.hint`)}</div>` : ""}
                </div>
              </div>
              <div class="panel-body settings-fields">
                ${fields.map(field => renderSettingField(field))}
              </div>
            </section>
          </div>`);

        on(root, "[data-settings-group]", "click", event => {
          settingsState.group = event.currentTarget.dataset.settingsGroup;
          renderSettings(root);
        });

        on(root, "[data-setting]", "input", event => onSettingInput(event.currentTarget, root));
        on(root, "[data-setting]", "change", event => onSettingInput(event.currentTarget, root));
        on(root, "[data-color-for]", "input", event => {
          const target = $(`[data-setting="${event.currentTarget.dataset.colorFor}"]`, root);
          if (!target) return;
          target.value = event.currentTarget.value.toUpperCase();
          onSettingInput(target, root);
        });

        $("#settings-save")?.addEventListener("click", event => saveSettings(event.currentTarget, root));
        $("#settings-discard")?.addEventListener("click", () => {
          settingsState.edits = {};
          settingsState.errors = {};
          renderSettings(root);
        });
      }

      function settingValue(field) {
        return field.key in settingsState.edits ? settingsState.edits[field.key] : field.value;
      }

      function renderSettingField(field) {
        const value = settingValue(field);
        const dirty = field.key in settingsState.edits;
        const error = settingsState.errors[field.key];
        const hint = settingHint(field);
        let control;
        switch (field.type) {
          case "bool":
            control = html`
              <label class="switch">
                <input type="checkbox" data-setting="${field.key}" data-type="bool" ${value === "true" ? "checked" : ""}>
                <span class="switch-track"></span>
                <span class="switch-label">${value === "true" ? t("common.on") : t("common.off")}</span>
              </label>`;
            break;
          case "int":
          case "double":
            control = html`<input type="number" step="${field.type === "double" ? "any" : "1"}" data-setting="${field.key}" value="${value}">`;
            break;
          case "text":
          case "items":
            control = html`<textarea rows="${field.type === "items" ? 4 : 5}" data-setting="${field.key}" spellcheck="false">${value}</textarea>`;
            break;
          case "choice":
            control = html`<select data-setting="${field.key}">${(field.options || []).map(item => option(item, item, value))}</select>`;
            break;
          case "secret":
            control = html`<input type="password" autocomplete="new-password" data-setting="${field.key}" value="${value}" placeholder="${field.configured ? t("settings.secret_configured") : t("settings.secret_missing")}">`;
            break;
          case "color":
            control = html`<div class="input-row"><input type="color" data-color-for="${field.key}" value="${/^#[0-9A-Fa-f]{6}$/.test(value) ? value : "#000000"}"><input data-setting="${field.key}" value="${value}" class="mono"></div>`;
            break;
          default:
            control = html`<input data-setting="${field.key}" value="${value}" ${field.type === "duration" ? 'placeholder="30d"' : ""}>`;
        }

        return html`
          <div class="setting ${dirty ? "dirty" : ""} ${error ? "has-error" : ""}">
            <div class="setting-label">
              <strong>${settingLabel(field)}</strong>
              <span class="mono muted small">${field.key}</span>
              ${field.restart ? chip(t("settings.restart_badge"), "warn") : ""}
            </div>
            <div class="setting-control">
              ${control}
              ${hint ? html`<div class="muted small">${hint}</div>` : ""}
              ${error ? html`<div class="field-error">${error}</div>` : ""}
            </div>
          </div>`;
      }

      function onSettingInput(element, root) {
        const key = element.dataset.setting;
        const field = settingsState.fields.find(item => item.key === key);
        if (!field) return;
        const value = field.type === "bool" ? (element.checked ? "true" : "false") : element.value;
        if (field.type === "bool") {
          const label = element.parentElement.querySelector(".switch-label");
          if (label) label.textContent = element.checked ? t("common.on") : t("common.off");
        }
        if (value === field.value && !(field.type === "secret" && value)) delete settingsState.edits[key];
        else settingsState.edits[key] = value;
        if (field.type === "secret" && !value) delete settingsState.edits[key];
        element.closest(".setting")?.classList.toggle("dirty", key in settingsState.edits);
        updateSettingsActions(root);
      }

      function updateSettingsActions(root) {
        const editCount = Object.keys(settingsState.edits).length;
        const save = $("#settings-save");
        const discard = $("#settings-discard");
        if (save) save.disabled = !editCount;
        if (discard) discard.disabled = !editCount;
        const chipHost = save?.parentElement;
        if (chipHost) {
          const existing = chipHost.querySelector(".chip");
          if (existing) existing.remove();
          if (editCount) chipHost.insertAdjacentHTML("afterbegin", String(chip(t("settings.pending", { count: editCount }), "warn")));
        }
        for (const button of $$("[data-settings-group]", root)) {
          const group = button.dataset.settingsGroup;
          const count = settingsState.fields.filter(field => field.group === group && field.key in settingsState.edits).length;
          const dot = button.querySelector(".dot");
          if (count && !dot) button.insertAdjacentHTML("beforeend", '<span class="dot"></span>');
          if (!count && dot) dot.remove();
        }
      }

      async function saveSettings(button, root) {
        const values = { ...settingsState.edits };
        if (!Object.keys(values).length) return;
        await run(button, async () => {
          const data = await api("/admin/api/settings", { method: "PUT", body: { values } });
          settingsState.fields = data.fields || settingsState.fields;
          settingsState.errors = {};
          for (const error of data.errors || []) {
            const separator = error.indexOf(":");
            if (separator > 0) settingsState.errors[error.slice(0, separator)] = error.slice(separator + 1).trim();
          }
          settingsState.edits = Object.fromEntries(Object.entries(values).filter(([key]) => key in settingsState.errors));
          settingsState.restartRequired = settingsState.restartRequired || Boolean(data.restart_required);
          state.cache.overview = null;
          renderSettings(root);
          if (data.errors?.length) throw new Error(t("settings.saved_with_errors", { applied: data.applied, errors: data.errors.length }));
          return data;
        }, data => t(data?.persisted ? "settings.saved" : "settings.saved_memory", { count: data?.applied ?? 0 }));
      }

""";
    }
}
