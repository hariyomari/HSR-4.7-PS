#!/usr/bin/env python3
"""Generate GM handbook files (Handbook <LANG>.txt) from a raw game-data tree (e.g. TurnBasedGameData).

Produces the same sections and format as Program/Handbook/HandbookGenerator.cs and
HandbookGenerator.CurrencyWar.cs without needing a raw-resource server boot or removing
Config/Resource.bin.

Split TextMap files (e.g. TextMapKR_0.json + TextMapKR_1.json) are merged into a single
Handbook <LANG>.txt per language, matching what WebServer/Controllers/HandbookRoutes.cs serves.

Usage:
  python scripts/generate-handbook.py [resource_dir] [--out Config/Handbook] [--all-lang | --lang EN --lang CHS]
"""

from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
import re
import sys
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent

LANGUAGE_ALIASES = {
    "CHS": "CHS",
    "CN": "CHS",
}

DEFAULT_EXCEL_DIRS = ["ExcelOutput", "ExcelOutputGameCore"]

ITEM_CONFIG_FILES = [
    ("ItemConfig.json", False),
    ("ItemConfigAvatar.json", False),
    ("ItemConfigAvatarLD.json", False),
    ("ItemConfigAvatarPlayerIcon.json", False),
    ("ItemConfigAvatarPlayerIcLD.json", False),
    ("ItemConfigAvatarRank.json", False),
    ("ItemConfigAvatarRankLD.json", False),
    ("ItemConfigBook.json", False),
    ("ItemConfigDisk.json", False),
    ("ItemConfigEquipment.json", False),
    ("ItemConfigRelic.json", False),
    ("ItemPlayerCard.json", False),
    ("ItemConfigTrainDynamic.json", True),
    ("ItemConfigAvatarSkin.json", False),
]

AVATAR_CONFIG_FILES = [
    "AvatarConfig.json",
    "AvatarConfigTrial.json",
    "AvatarConfigLD.json",
]

STAGE_CONFIG_FILES = [
    "StageConfig.json",
    "StageTestConfig.json",
]

RICH_TEXT_TAG = re.compile(r"<[^>]+>")
SPLIT_LANG_SUFFIX = re.compile(r"^(.+?)_(\d+)$")
PERM_CONST_RE = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]*)"\s*;')
CMD_ATTR_RE = re.compile(r"\[CommandInfo\s*\((.*?)\)\]", re.DOTALL)


def normalize_lang(lang: str) -> str:
    upper = lang.strip().upper()
    return LANGUAGE_ALIASES.get(upper, upper)


def resolve_default_resources() -> Path | None:
    candidates: list[Path] = []
    config_path = REPO_ROOT / "Config.json"
    if config_path.is_file():
        try:
            cfg = json.loads(config_path.read_text(encoding="utf-8"))
            res_path = cfg.get("Path", {}).get("ResourcePath")
            if isinstance(res_path, str) and res_path.strip():
                p = Path(res_path)
                candidates.append(p if p.is_absolute() else REPO_ROOT / p)
        except (OSError, json.JSONDecodeError):
            pass

    for fallback in ("Resources", "TurnBasedGameData"):
        candidate = REPO_ROOT / fallback
        if candidate not in candidates:
            candidates.append(candidate)

    for candidate in candidates:
        if (candidate / "TextMap").is_dir():
            return candidate
    return None


def find_excel_file(resources: Path, excel_dirs: list[str], filename: str) -> Path | None:
    for rel_dir in excel_dirs:
        candidate = resources / rel_dir / filename
        if candidate.is_file():
            return candidate
    return None


def iter_excel_rows(data: Any) -> list[dict[str, Any]]:
    if isinstance(data, list):
        return [row for row in data if isinstance(row, dict)]
    if isinstance(data, dict):
        rows: list[dict[str, Any]] = []
        for value in data.values():
            if not isinstance(value, dict):
                continue
            # Detect nested dict-of-dicts (e.g. RogueMazeBuff keyed by ID -> Lv -> row)
            if value and all(isinstance(v, dict) for v in value.values()) and not any(
                k in value for k in ("ID", "AvatarID", "StageID", "MainMissionID", "SubMissionID", "MiracleID", "OrbID")
            ):
                rows.extend(v for v in value.values() if isinstance(v, dict))
            else:
                rows.append(value)
        return rows
    return []


def load_excel_rows(resources: Path, excel_dirs: list[str], filename: str, optional: bool = False) -> list[dict[str, Any]]:
    path = find_excel_file(resources, excel_dirs, filename)
    if path is None:
        if not optional:
            print(f"warning: missing Excel table {filename}", file=sys.stderr)
        return []
    with path.open(encoding="utf-8") as handle:
        return iter_excel_rows(json.load(handle))


def extract_hash(row: dict[str, Any], field: str) -> int | None:
    obj = row.get(field)
    if not isinstance(obj, dict):
        return None
    val = obj.get("Hash")
    if val is None:
        val = obj.get("Hash64")
    if val is None:
        return None
    try:
        return int(val)
    except (TypeError, ValueError):
        return None


def extract_hash_or_zero(row: dict[str, Any], field: str) -> int:
    h = extract_hash(row, field)
    return 0 if h is None else h


def to_int(row: dict[str, Any], field: str, default: int = 0) -> int:
    val = row.get(field)
    if val is None:
        return default
    try:
        return int(val)
    except (TypeError, ValueError):
        return default


class I18nStore:
    def __init__(self, languages_dir: Path) -> None:
        self.languages_dir = languages_dir
        self._cache: dict[str, dict[str, str]] = {}

    def _load(self, lang: str) -> dict[str, str]:
        code = "CHS" if lang.upper() == "CN" else lang.upper()
        if code in self._cache:
            return self._cache[code]
        path = self.languages_dir / f"{code}.json"
        if path.is_file():
            with path.open(encoding="utf-8") as handle:
                data = json.load(handle)
            self._cache[code] = {str(k): str(v) for k, v in data.items()}
            return self._cache[code]
        if code != "EN":
            fallback = self._load("EN")
            self._cache[code] = fallback
            return fallback
        self._cache[code] = {}
        return self._cache[code]

    def translate(self, lang: str, key: str, *args: Any) -> str:
        table = self._load(lang)
        text = table.get(key, key)
        for index, arg in enumerate(args):
            text = text.replace(f"{{{index}}}", str(arg))
        return text


def split_csharp_args(arg_text: str) -> list[str]:
    parts: list[str] = []
    buf: list[str] = []
    in_string = False
    escape = False
    bracket_depth = 0
    brace_depth = 0
    paren_depth = 0

    for ch in arg_text:
        if in_string:
            buf.append(ch)
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            continue

        if ch == '"':
            in_string = True
            buf.append(ch)
        elif ch == "[":
            bracket_depth += 1
            buf.append(ch)
        elif ch == "]":
            bracket_depth -= 1
            buf.append(ch)
        elif ch == "{":
            brace_depth += 1
            buf.append(ch)
        elif ch == "}":
            brace_depth -= 1
            buf.append(ch)
        elif ch == "(":
            paren_depth += 1
            buf.append(ch)
        elif ch == ")":
            paren_depth -= 1
            buf.append(ch)
        elif ch == "," and bracket_depth == 0 and brace_depth == 0 and paren_depth == 0:
            parts.append("".join(buf).strip())
            buf.clear()
        else:
            buf.append(ch)

    tail = "".join(buf).strip()
    if tail:
        parts.append(tail)
    return parts


def parse_csharp_string(expr: str, permissions: dict[str, str]) -> str:
    expr = expr.strip()
    if len(expr) >= 2 and expr[0] == '"' and expr[-1] == '"':
        return json.loads(expr)
    if expr.startswith("CommandPermissions."):
        const_name = expr.split(".", 1)[1].strip()
        if const_name in permissions:
            return permissions[const_name]
    return ""


def parse_csharp_string_array(expr: str) -> list[str]:
    return [json.loads(match) for match in re.findall(r'"(?:\\.|[^"\\])*"', expr)]


def load_commands(commands_dir: Path, permissions_file: Path) -> list[dict[str, Any]]:
    permissions: dict[str, str] = {}
    if permissions_file.is_file():
        for name, value in PERM_CONST_RE.findall(permissions_file.read_text(encoding="utf-8")):
            permissions[name] = value

    commands: list[dict[str, Any]] = []
    if not commands_dir.is_dir():
        print(f"warning: commands directory not found: {commands_dir}", file=sys.stderr)
        return commands

    for cs_file in sorted(commands_dir.rglob("*.cs")):
        source = cs_file.read_text(encoding="utf-8")
        for match in CMD_ATTR_RE.finditer(source):
            raw_args = split_csharp_args(match.group(1))
            positional: list[str] = []
            named: dict[str, str] = {}
            for item in raw_args:
                named_match = re.match(r"^(\w+)\s*:\s*(.*)$", item, re.DOTALL)
                if named_match:
                    named[named_match.group(1)] = named_match.group(2).strip()
                else:
                    positional.append(item)

            if len(positional) < 3:
                continue

            name = parse_csharp_string(positional[0], permissions)
            description = parse_csharp_string(positional[1], permissions)
            usage = parse_csharp_string(positional[2], permissions)
            aliases: list[str] = []
            permission = ""

            if len(positional) >= 4:
                fourth = positional[3].strip()
                if fourth.startswith("[") or fourth.startswith("new"):
                    aliases = parse_csharp_string_array(fourth)
                    if len(positional) >= 6:
                        permission = parse_csharp_string(positional[5], permissions)
                elif len(positional) >= 5:
                    permission = parse_csharp_string(positional[4], permissions)

            if "alias" in named:
                aliases = parse_csharp_string_array(named["alias"])
            if "permission" in named:
                permission = parse_csharp_string(named["permission"], permissions)

            if name:
                commands.append(
                    {
                        "name": name,
                        "description": description,
                        "usage": usage,
                        "aliases": aliases,
                        "permission": permission,
                    }
                )

    commands.sort(key=lambda cmd: cmd["name"].lower())
    return commands


def discover_textmaps(textmap_dir: Path) -> dict[str, list[Path]]:
    groups: dict[str, list[tuple[int, Path]]] = {}
    for file_path in sorted(textmap_dir.iterdir()):
        if not file_path.is_file() or file_path.suffix.lower() != ".json":
            continue
        if file_path.name.lower().startswith("textmapmain"):
            continue
        if not file_path.name.startswith("TextMap"):
            continue

        raw_code = file_path.stem[len("TextMap") :]
        if not raw_code:
            continue

        part_match = SPLIT_LANG_SUFFIX.match(raw_code)
        if part_match:
            lang = normalize_lang(part_match.group(1))
            part_index = int(part_match.group(2))
        else:
            lang = normalize_lang(raw_code)
            part_index = -1

        groups.setdefault(lang, []).append((part_index, file_path))

    result: dict[str, list[Path]] = {}
    for lang, items in sorted(groups.items()):
        # Prefer CHS over CN if both exist for the same language code.
        items.sort(key=lambda entry: (entry[0], 0 if "CHS" in entry[1].name.upper() else 1, entry[1].name))
        # If non-split files exist for both CHS and CN, only keep the preferred one (matches C# HashSet).
        if all(part == -1 for part, _ in items):
            result[lang] = [items[0][1]]
        else:
            result[lang] = [p for _, p in items]
    return result


def load_textmap(files: list[Path]) -> dict[str, str]:
    merged: dict[str, str] = {}
    for path in files:
        with path.open(encoding="utf-8") as handle:
            data = json.load(handle)
        if isinstance(data, dict):
            merged.update(data)
    return merged


def lookup_name(hash_val: int, text_map: dict[str, str], fallback: dict[str, str]) -> tuple[bool, str]:
    key = str(hash_val)
    if key in text_map:
        return True, text_map[key]
    if key in fallback:
        return True, fallback[key]
    return False, f"[{hash_val}]"


def get_name(hash_val: int, text_map: dict[str, str], fallback: dict[str, str]) -> str:
    _, name = lookup_name(hash_val, text_map, fallback)
    return name


def clean_text(value: str) -> str:
    return RICH_TEXT_TAG.sub("", value).replace("\\n", " ").replace("\n", " ").strip()


class GameTables:
    """Loaded Excel tables shared across all output languages."""

    def __init__(self, resources: Path, excel_dirs: list[str]) -> None:
        self.resources = resources
        self.excel_dirs = excel_dirs

        # Avatars: first-wins across AvatarConfig, AvatarConfigTrial, AvatarConfigLD
        self.avatars: dict[int, int] = {}
        for filename in AVATAR_CONFIG_FILES:
            for row in load_excel_rows(resources, excel_dirs, filename):
                avatar_id = to_int(row, "AvatarID")
                if avatar_id not in self.avatars:
                    self.avatars[avatar_id] = extract_hash_or_zero(row, "AvatarName")

        # Items: last-wins across all 14 ItemConfig* tables
        self.items: dict[int, int] = {}
        for filename, optional in ITEM_CONFIG_FILES:
            for row in load_excel_rows(resources, excel_dirs, filename, optional=optional):
                item_id = to_int(row, "ID")
                self.items[item_id] = extract_hash_or_zero(row, "ItemName")

        # Stages: first-wins across StageConfig, StageTestConfig
        self.stages: dict[int, int] = {}
        for filename in STAGE_CONFIG_FILES:
            for row in load_excel_rows(resources, excel_dirs, filename):
                stage_id = to_int(row, "StageID")
                if stage_id not in self.stages:
                    self.stages[stage_id] = extract_hash_or_zero(row, "StageName")

        # Main & Sub missions: last-wins
        self.main_missions: dict[int, int] = {}
        for row in load_excel_rows(resources, excel_dirs, "MainMission.json"):
            self.main_missions[to_int(row, "MainMissionID")] = extract_hash_or_zero(row, "Name")

        self.sub_missions: dict[int, int] = {}
        for row in load_excel_rows(resources, excel_dirs, "SubMission.json"):
            self.sub_missions[to_int(row, "SubMissionID")] = extract_hash_or_zero(row, "TargetText")

        # Rogue Maze Buffs: key = ID * 100 + Lv
        self.rogue_buffs: dict[int, tuple[int, int]] = {}
        for row in load_excel_rows(resources, excel_dirs, "RogueMazeBuff.json"):
            buff_id = to_int(row, "ID")
            lv = to_int(row, "Lv")
            key = buff_id * 100 + lv
            if key not in self.rogue_buffs:
                self.rogue_buffs[key] = (extract_hash_or_zero(row, "BuffName"), lv)

        # Rogue Miracles: display lookup + last-wins by MiracleID
        miracle_displays: dict[int, int] = {}
        for row in load_excel_rows(resources, excel_dirs, "RogueMiracleDisplay.json"):
            display_id = to_int(row, "MiracleDisplayID")
            if display_id not in miracle_displays:
                miracle_displays[display_id] = extract_hash_or_zero(row, "MiracleName")

        self.rogue_miracles: dict[int, int] = {}
        for row in load_excel_rows(resources, excel_dirs, "RogueMiracle.json"):
            miracle_id = to_int(row, "MiracleID")
            display_id = to_int(row, "MiracleDisplayID")
            name_hash = miracle_displays.get(display_id, 0) if display_id != 0 else 0
            self.rogue_miracles[miracle_id] = name_hash

        # Currency War (GridFight) tables
        self.gf_roles: dict[int, dict[str, Any]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightRoleBasicInfo.json", optional=True):
            self.gf_roles.setdefault(to_int(row, "ID"), row)

        self.gf_traits: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightTraitBasicInfo.json", optional=True
        )

        self.gf_equipment: dict[int, dict[str, Any]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightEquipment.json", optional=True):
            self.gf_equipment.setdefault(to_int(row, "ID"), row)

        self.gf_consumables: dict[int, dict[str, Any]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightConsumables.json", optional=True):
            self.gf_consumables.setdefault(to_int(row, "ID"), row)

        self.gf_items: dict[int, int] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightItems.json", optional=True):
            item_id = to_int(row, "ID")
            if item_id not in self.gf_items:
                self.gf_items[item_id] = extract_hash_or_zero(row, "ItemName")

        self.gf_season_items: dict[int, list[int]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightSeasonItem.json", optional=True):
            item_id = to_int(row, "ItemID")
            season_id = to_int(row, "SeasonID")
            seasons = self.gf_season_items.setdefault(item_id, [])
            if season_id not in seasons:
                seasons.append(season_id)

        self.gf_orbs: dict[int, dict[str, Any]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightOrb.json", optional=True):
            self.gf_orbs.setdefault(to_int(row, "OrbID"), row)

        self.gf_portals: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightPortalBuff.json", optional=True
        )
        self.gf_season_portals: dict[int, list[int]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightSeasonPortal.json", optional=True):
            portal_id = to_int(row, "PortalID")
            season_id = to_int(row, "SeasonID")
            seasons = self.gf_season_portals.setdefault(portal_id, [])
            if season_id not in seasons:
                seasons.append(season_id)

        self.gf_augments_raw: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightAugment.json", optional=True
        )
        self.gf_augments_by_id: dict[int, dict[str, Any]] = {}
        for row in self.gf_augments_raw:
            self.gf_augments_by_id.setdefault(to_int(row, "ID"), row)

        self.gf_season_augments: dict[int, list[int]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightSeasonAugment.json", optional=True):
            augment_id = to_int(row, "AugmentID")
            season_id = to_int(row, "SeasonID")
            seasons = self.gf_season_augments.setdefault(augment_id, [])
            if season_id not in seasons:
                seasons.append(season_id)

        self.gf_pet_pals: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightPetPal.json", optional=True
        )
        self.gf_pet_pal_effs: dict[int, dict[str, Any]] = {
            to_int(row, "ID"): row
            for row in load_excel_rows(resources, excel_dirs, "GridFightPetPalEff.json", optional=True)
        }

        self.gf_division_info: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightDivisionInfo.json", optional=True
        )
        self.gf_division_level_show: list[dict[str, Any]] = load_excel_rows(
            resources, excel_dirs, "GridFightDivisionLevelShow.json", optional=True
        )

        self.gf_stage_routes: dict[int, dict[int, dict[str, Any]]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightStageRoute.json", optional=True):
            route_id = to_int(row, "ID")
            chapter_id = to_int(row, "ChapterID")
            section_id = to_int(row, "SectionID")
            self.gf_stage_routes.setdefault(route_id, {})[(chapter_id << 8) | section_id] = row

        self.gf_node_templates: dict[int, dict[str, Any]] = {}
        for row in load_excel_rows(resources, excel_dirs, "GridFightNodeTemplate.json", optional=True):
            self.gf_node_templates.setdefault(to_int(row, "NodeTemplateID"), row)


def append_translated_block(lines: list[str], i18n: I18nStore, lang: str, label: str, key: str) -> None:
    text = i18n.translate(lang, key).replace("\r\n", "\n").replace("\r", "\n").strip()
    lines.append(f"  {label}:")
    for line in text.split("\n"):
        lines.append(f"    {line.rstrip()}")


def append_commands_section(lines: list[str], i18n: I18nStore, lang: str, commands: list[dict[str, Any]]) -> None:
    lines.append(i18n.translate(lang, "Server.ServerInfo.HandbookCommandNotesTitle"))
    for note_key in (
        "Server.ServerInfo.HandbookCommandNoteTarget",
        "Server.ServerInfo.HandbookCommandNoteQuote",
        "Server.ServerInfo.HandbookCommandNoteCaseInsensitive",
        "Server.ServerInfo.HandbookCommandNoteRequiredOptional",
        "Server.ServerInfo.HandbookCommandNoteOptionStyle",
        "Server.ServerInfo.HandbookCommandNoteLongOptionAliases",
    ):
        lines.append("  - " + i18n.translate(lang, note_key))
    lines.append("")

    desc_label = i18n.translate(lang, "Server.ServerInfo.HandbookDescriptionLabel")
    usage_label = i18n.translate(lang, "Server.ServerInfo.HandbookUsageLabel")
    aliases_label = i18n.translate(lang, "Server.ServerInfo.HandbookAliasesLabel")
    perm_label = i18n.translate(lang, "Server.ServerInfo.HandbookPermissionLabel")

    for cmd in commands:
        lines.append("/" + cmd["name"])
        append_translated_block(lines, i18n, lang, desc_label, cmd["description"])
        append_translated_block(lines, i18n, lang, usage_label, cmd["usage"])
        if cmd["aliases"]:
            lines.append("  " + aliases_label + ": " + ", ".join("/" + a for a in cmd["aliases"]))
        if cmd["permission"].strip():
            lines.append("  " + perm_label + ": " + cmd["permission"])
        lines.append("")


def append_currency_war_sections(
    lines: list[str],
    i18n: I18nStore,
    lang: str,
    tables: GameTables,
    text_map: dict[str, str],
    fallback: dict[str, str],
) -> None:
    def t(suffix: str, *args: Any) -> str:
        return i18n.translate(lang, "Server.ServerInfo." + suffix, *args)

    def name_by_hash(hash_val: int) -> str:
        return clean_text(get_name(hash_val, text_map, fallback))

    def has_hash(hash_val: int) -> bool:
        key = str(hash_val)
        return key in text_map or key in fallback

    def row_name(row: dict[str, Any], field: str) -> str:
        h = extract_hash(row, field)
        return name_by_hash(h) if h is not None else ""

    def row_has_name(row: dict[str, Any], field: str) -> bool:
        h = extract_hash(row, field)
        return h is not None and has_hash(h)

    def season_tag(season: int) -> str:
        return t("HandbookCurrencyWarShared") if season == 0 else f"S{season}"

    def seasons_tag(seasons: list[int]) -> str:
        return t("HandbookCurrencyWarShared") if not seasons else "/".join(f"S{s}" for s in sorted(seasons))

    def start_section(title_key: str) -> None:
        lines.append("")
        lines.append("#" + i18n.translate(lang, title_key))
        lines.append("")

    # Role
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarRole")
    trait_names: dict[int, str] = {}
    for row in tables.gf_traits:
        tid = to_int(row, "ID")
        if tid not in trait_names:
            trait_names[tid] = row_name(row, "TraitName")
    for role in sorted(
        tables.gf_roles.values(),
        key=lambda r: (to_int(r, "SeasonID"), to_int(r, "Rarity"), to_int(r, "ID")),
    ):
        avatar_id = to_int(role, "AvatarID")
        if avatar_id not in tables.avatars:
            continue
        tags = [season_tag(to_int(role, "SeasonID")), t("HandbookCurrencyWarCost", to_int(role, "Rarity"))]
        trait_list = role.get("TraitList") or []
        if trait_list:
            tags.append(
                t("HandbookCurrencyWarTraits")
                + ": "
                + "/".join(trait_names.get(int(tid), str(tid)) for tid in trait_list)
            )
        if not role.get("IsInPool", False):
            tags.append(t("HandbookCurrencyWarNotInPool"))
        lines.append(f"{to_int(role, 'ID')}: {name_by_hash(tables.avatars[avatar_id])} [{' · '.join(tags)}]")

    # Trait
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarTrait")
    for row in sorted(tables.gf_traits, key=lambda r: (to_int(r, "SeasonID"), to_int(r, "ID"))):
        tags = [season_tag(to_int(row, "SeasonID"))]
        trait_type = row.get("TraitType")
        if isinstance(trait_type, str) and trait_type:
            tags.append(t("HandbookCurrencyWarTrait" + trait_type))
        lines.append(f"{to_int(row, 'ID')}: {row_name(row, 'TraitName')} [{' · '.join(tags)}]")

    # Equipment
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarEquipment")
    equip_entries = [
        (
            to_int(row, "ID"),
            str(row.get("EquipCategory") or "None"),
            tables.gf_season_items.get(to_int(row, "ID"), []),
        )
        for row in tables.gf_equipment.values()
    ]
    equip_entries.sort(key=lambda item: (min(item[2]) if item[2] else 0xFFFFFFFF, item[0]))
    for item_id, category, seasons in equip_entries:
        if item_id not in tables.gf_items:
            continue
        tags = [seasons_tag(seasons), category]
        lines.append(f"{item_id}: {name_by_hash(tables.gf_items[item_id])} [{' · '.join(tags)}]")

    # Consumable
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarConsumable")
    consumable_entries = [
        (to_int(row, "ID"), tables.gf_season_items.get(to_int(row, "ID"), []))
        for row in tables.gf_consumables.values()
    ]
    consumable_entries.sort(key=lambda item: (min(item[1]) if item[1] else 0xFFFFFFFF, item[0]))
    for item_id, seasons in consumable_entries:
        if item_id not in tables.gf_items:
            continue
        lines.append(f"{item_id}: {name_by_hash(tables.gf_items[item_id])} [{seasons_tag(seasons)}]")

    # Orb
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarOrb")
    for orb in sorted(tables.gf_orbs.values(), key=lambda r: to_int(r, "OrbID")):
        orb_id = to_int(orb, "OrbID")
        bonus_id = to_int(orb, "BonusID")
        orb_type = str(orb.get("Type") or "None")
        type_name = t("HandbookCurrencyWarOrb" + orb_type)
        bonus = f"{t('HandbookCurrencyWarBonus')} {bonus_id}"
        orb_hash = extract_hash_or_zero(orb, "OrbName")
        if orb_hash != 0 and has_hash(orb_hash):
            lines.append(f"{orb_id}: {name_by_hash(orb_hash)} [{type_name} · {bonus}]")
        else:
            lines.append(f"{orb_id}: {type_name} [{bonus}]")

    # Portal
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarPortal")
    for row in sorted(tables.gf_portals, key=lambda r: to_int(r, "ID")):
        portal_id = to_int(row, "ID")
        seasons = tables.gf_season_portals.get(portal_id, [])
        lines.append(f"{portal_id}: {row_name(row, 'PortalBuffTitle')} [{seasons_tag(seasons)}]")

    # Augment
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarAugment")
    for row in sorted(tables.gf_augments_raw, key=lambda r: to_int(r, "ID")):
        aug_id = to_int(row, "ID")
        tags = [seasons_tag(tables.gf_season_augments.get(aug_id, []))]
        aug = tables.gf_augments_by_id.get(aug_id)
        quality = str(aug.get("Quality") or "None") if aug else "None"
        if quality != "None":
            tags.append(t("HandbookCurrencyWarQuality" + quality))
        lines.append(f"{aug_id}: {row_name(row, 'HexName')} [{' · '.join(tags)}]")

    # Partner (PetPal)
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarPetPal")
    partner_label = t("HandbookCurrencyWarPartner")
    effects_label = t("HandbookCurrencyWarEffects")
    for partner in sorted(tables.gf_pet_pals, key=lambda r: to_int(r, "ID")):
        pid = to_int(partner, "ID")
        pname = row_name(partner, "Name")
        effect_ids = [int(eid) for eid in (partner.get("EffIDList") or [])]
        eff_summary = "-" if not effect_ids else "/".join(str(eid) for eid in effect_ids)
        lines.append(f"{pid}: {pname} [{partner_label} · {effects_label}: {eff_summary}]")
        for eff_id in effect_ids:
            eff = tables.gf_pet_pal_effs.get(eff_id)
            desc = "" if eff is None else row_name(eff, "SwitchDesc")
            lines.append(f"  {eff_id}: {desc} [{partner_label} {pid} {pname}]")

    # Division
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarDivision")
    tier_names: dict[tuple[int, int], str] = {}
    for row in tables.gf_division_level_show:
        key = (to_int(row, "SeasonID"), to_int(row, "DivisionLevel"))
        if key not in tier_names:
            tier_names[key] = row_name(row, "DivisionNameWithNum")

    for row in sorted(tables.gf_division_info, key=lambda r: (to_int(r, "SeasonID"), to_int(r, "ID"))):
        season = to_int(row, "SeasonID")
        level = to_int(row, "DivisionLevel")
        progress = to_int(row, "Progress")
        name = row_name(row, "DivisionName") if row_has_name(row, "DivisionName") else tier_names.get((season, level), "")
        name = name.replace("#1", str(progress))
        tags = [
            season_tag(season),
            t("HandbookCurrencyWarTutorial") if level == 0 else t("HandbookCurrencyWarDivisionLevel", level, progress),
        ]
        if row.get("IsPromotion") is True:
            tags.append(t("HandbookCurrencyWarPromotion"))
        lines.append(f"{to_int(row, 'ID')}: {name} [{' · '.join(tags)}]")

    # Route
    start_section("Server.ServerInfo.HandbookSectionCurrencyWarRoute")
    seasons = sorted({to_int(row, "SeasonID") for row in tables.gf_division_info if to_int(row, "SeasonID") > 0})
    for route_id, nodes_by_key in sorted(tables.gf_stage_routes.items()):
        route_seasons = [s for s in seasons if route_id // 1000 == s]
        tags = [
            seasons_tag(route_seasons) if route_seasons else t("HandbookCurrencyWarTutorialRoute"),
            t("HandbookCurrencyWarNodes", len(nodes_by_key)),
        ]
        node_labels: list[str] = []
        for node in sorted(nodes_by_key.values(), key=lambda r: (to_int(r, "ChapterID"), to_int(r, "SectionID"))):
            template = tables.gf_node_templates.get(to_int(node, "NodeTemplateID"))
            if template is None:
                label = "?"
            else:
                node_type = str(template.get("NodeType") or "GridFightNodeNone")
                label = t("HandbookCurrencyWarNode" + node_type)
                if to_int(template, "IsAugment") != 0:
                    label += "+" + t("HandbookCurrencyWarAugmentNode")
            node_labels.append(f"{to_int(node, 'ChapterID')}-{to_int(node, 'SectionID')} {label}")
        lines.append(f"{route_id}: [{' · '.join(tags)}] {' · '.join(node_labels)}")


def render_handbook(
    lang: str,
    timestamp: str,
    i18n: I18nStore,
    commands: list[dict[str, Any]],
    tables: GameTables,
    text_map: dict[str, str],
    fallback: dict[str, str],
) -> str:
    lines: list[str] = [
        "#" + i18n.translate(lang, "Server.ServerInfo.HandbookGeneratedAt", timestamp),
        "",
        "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionCommand"),
        "",
    ]
    append_commands_section(lines, i18n, lang, commands)

    # Avatar
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionAvatar"), ""])
    for avatar_id, avatar_hash in tables.avatars.items():
        resolved, name = lookup_name(avatar_hash, text_map, fallback)
        if not resolved and avatar_id in tables.items:
            resolved, name = lookup_name(tables.items[avatar_id], text_map, fallback)
        lines.append(f"{avatar_id}: {name}")

    # Item
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionItem"), ""])
    for item_id, item_hash in tables.items.items():
        lines.append(f"{item_id}: {get_name(item_hash, text_map, fallback)}")

    # StageId
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionStageId"), ""])
    for stage_id, stage_hash in tables.stages.items():
        lines.append(f"{stage_id}: {get_name(stage_hash, text_map, fallback)}")

    # MainMission
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionMainMission"), ""])
    for mission_id, mission_hash in tables.main_missions.items():
        lines.append(f"{mission_id}: {get_name(mission_hash, text_map, fallback)}")

    # SubMission
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionSubMission"), ""])
    for sub_id, sub_hash in tables.sub_missions.items():
        lines.append(f"{sub_id}: {get_name(sub_hash, text_map, fallback)}")

    # RogueBuff
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionRogueBuff"), ""])
    for key, (buff_hash, lv) in tables.rogue_buffs.items():
        lines.append(f"{key}: {get_name(buff_hash, text_map, fallback)} --- Level:{lv}")

    # RogueMiracle
    lines.extend(["", "#" + i18n.translate(lang, "Server.ServerInfo.HandbookSectionRogueMiracle"), ""])
    for miracle_id, miracle_hash in tables.rogue_miracles.items():
        lines.append(f"{miracle_id}: {get_name(miracle_hash, text_map, fallback)}")

    # Currency War (GridFight)
    append_currency_war_sections(lines, i18n, lang, tables, text_map, fallback)

    lines.append("")
    return "\n".join(lines) + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "resources",
        nargs="?",
        type=Path,
        default=None,
        help="game data tree containing ExcelOutput/ and TextMap/ (default: auto-detect from Config.json, Resources, or TurnBasedGameData)",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=REPO_ROOT / "Config" / "Handbook",
        help="output directory for Handbook <LANG>.txt files (default: Config/Handbook)",
    )
    lang_group = parser.add_mutually_exclusive_group()
    lang_group.add_argument(
        "--lang",
        action="append",
        default=[],
        help="language code(s) to generate (repeatable or comma-separated, e.g. --lang EN --lang CHS)",
    )
    lang_group.add_argument(
        "--all-lang",
        action="store_true",
        help="generate handbooks for all available languages found in TextMap/ (default when --lang is omitted)",
    )
    parser.add_argument(
        "--fallback-lang",
        default="EN",
        help="fallback TextMap language for missing hashes (default: EN)",
    )
    parser.add_argument(
        "--languages-dir",
        type=Path,
        default=REPO_ROOT / "Config" / "Languages",
        help="server i18n directory (default: Config/Languages)",
    )
    parser.add_argument(
        "--commands-dir",
        type=Path,
        default=REPO_ROOT / "Command" / "Command" / "Cmd",
        help="C# command source directory (default: Command/Command/Cmd)",
    )
    parser.add_argument(
        "--permissions-file",
        type=Path,
        default=REPO_ROOT / "Common" / "Command" / "CommandPermissions.cs",
        help="C# permissions constants file (default: Common/Command/CommandPermissions.cs)",
    )
    parser.add_argument(
        "--excel-dir",
        action="append",
        default=[],
        help="Excel output subdirectory relative to resources (repeatable; default: ExcelOutput, ExcelOutputGameCore)",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    resources: Path | None = args.resources
    if resources is None:
        resources = resolve_default_resources()
        if resources is None:
            sys.exit(
                "Could not find a resource directory with TextMap/. "
                "Pass it explicitly, e.g.: python scripts/generate-handbook.py TurnBasedGameData"
            )

    textmap_dir = resources / "TextMap"
    if not textmap_dir.is_dir():
        sys.exit(f"{resources} needs a TextMap/ directory")

    excel_dirs = args.excel_dir or DEFAULT_EXCEL_DIRS
    if not any((resources / d).is_dir() for d in excel_dirs):
        sys.exit(f"{resources} needs at least one Excel directory ({', '.join(excel_dirs)})")

    textmap_groups = discover_textmaps(textmap_dir)
    if not textmap_groups:
        sys.exit(f"No TextMap<LANG>.json files found in {textmap_dir}")

    requested_langs: list[str] = []
    for raw in args.lang:
        for token in raw.split(","):
            token = token.strip()
            if token:
                code = normalize_lang(token)
                if code not in requested_langs:
                    requested_langs.append(code)

    if args.all_lang or not requested_langs:
        target_langs = list(textmap_groups.keys())
    else:
        missing = [code for code in requested_langs if code not in textmap_groups]
        if missing:
            sys.exit(
                f"Requested language(s) not found in {textmap_dir}: {', '.join(missing)} "
                f"(available: {', '.join(sorted(textmap_groups))})"
            )
        target_langs = requested_langs

    fallback_lang = normalize_lang(args.fallback_lang)
    fallback_map: dict[str, str] | None = None
    if fallback_lang in textmap_groups:
        fallback_map = load_textmap(textmap_groups[fallback_lang])
    else:
        print(f"warning: fallback TextMap for '{fallback_lang}' not found; falling back to each target language", file=sys.stderr)

    print(f"Loading Excel tables from {resources}...")
    tables = GameTables(resources, excel_dirs)
    commands = load_commands(args.commands_dir, args.permissions_file)
    i18n = I18nStore(args.languages_dir)
    timestamp = datetime.now().strftime("%Y/%m/%d %H:%M")

    args.out.mkdir(parents=True, exist_ok=True)
    for lang in target_langs:
        text_map = fallback_map if (lang == fallback_lang and fallback_map is not None) else load_textmap(textmap_groups[lang])
        effective_fallback = fallback_map if fallback_map is not None else text_map
        content = render_handbook(lang, timestamp, i18n, commands, tables, text_map, effective_fallback)
        out_path = args.out / f"Handbook {lang}.txt"
        out_path.write_text(content, encoding="utf-8", newline="\n")
        print(f"wrote {out_path} ({len(content):,} chars)")

    print(
        f"Done: {len(target_langs)} handbook(s), {len(commands)} commands, "
        f"{len(tables.avatars)} avatars, {len(tables.items)} items, "
        f"{len(tables.stages)} stages, {len(tables.main_missions)} main missions, "
        f"{len(tables.sub_missions)} sub missions."
    )


if __name__ == "__main__":
    main()
