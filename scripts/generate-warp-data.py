#!/usr/bin/env python3
"""Generate the warp Story-mode data files from a raw game-data tree (e.g. TurnBasedGameData).

Writes two files (default: Config/Custom):

  WarpStoryGates.json  version -> main mission ids of that version's Trailblaze chapters. A player has reached a
                       version once any of them is accepted or finished.
  WarpItemNames.json   names of every character and light cone, in English and Simplified Chinese, for the admin
                       Warps page.

The game data groups main missions into chapters (MainMission.ChapterID) but does not say which version shipped a
chapter. CHAPTER_VERSIONS below is that mapping, taken from the "Trailblaze Mission/Change History" page of the Honkai:
Star Rail Fandom wiki (https://honkai-star-rail.fandom.com/wiki/Trailblaze_Mission/Change_History, read 2026-11-21).
A new version needs one new line here; the script fails when the data has a story chapter the table does not cover.

Usage: python scripts/generate-warp-data.py <resource dir> [--out <dir>]
"""

import argparse
import json
import re
import sys
from pathlib import Path

# ChapterID -> version that added the chapter. 0.x beta chapters all shipped at the 1.0 launch.
CHAPTER_VERSIONS = {
    100001: "1.0",  # Today Is Yesterday's Tomorrow (Herta Space Station)
    101001: "1.0",  # In the Withering Wintry Night (Jarilo-VI)
    101002: "1.0",  # In the Sweltering Morning Sun (Jarilo-VI)
    102001: "1.0",  # Windswept Wanderlust (Xianzhou Luofu)
    102002: "1.2",  # Topclouded Towerthrust
    102003: "1.3",  # Karmic Clouds Faded, War Banners Folded
    103001: "2.0",  # The Sound and the Fury (Penacony)
    103002: "2.1",  # Cat Among Pigeons
    103003: "2.2",  # In Our Time
    103004: "2.3",  # Farewell, Penacony
    103006: "2.7",  # A New Venture on the Eighth Dawn
    104001: "3.0",  # Heroic Saga of Flame-Chase (Amphoreus)
    104002: "3.1",  # Light Slips the Gate, Shadow Greets the Throne
    104003: "3.2",  # Through the Petals in the Land of Repose
    104004: "3.3",  # The Fall at Dawn's Rise
    104005: "3.4",  # For the Sun is Set to Die
    104007: "3.5",  # Before Their Deaths
    104008: "3.6",  # Back to Earth in Evernight
    104010: "3.7",  # As Tomorrow Became Yesterday
    104009: "3.8",  # Memories are the Prelude to Dreams (Penacony)
    105001: "4.0",  # Welcome to Arcadia (Planarcadia)
    105002: "4.1",  # Unraveled for Daybreak
    105003: "4.2",  # So Laughed the Masses
    105004: "4.3",  # The Lethe Below the Living
    105005: "4.4",  # In Ravages Does the Whistle Sound
    105008: "4.5",  # To Roll the Stars in Astropolis
    105009: "4.6",  # Dance With the Beast Before Moonrise
    105010: "4.7",  # Now, Lay Laughter to All Stars
}

# Main-type chapters that are not story: Trial of Equilibrium (world level ascension).
NOT_STORY_CHAPTERS = {100000}

# Main missions filed under a story chapter that are not part of it: 4030001 "Simulated Universe: First Closed Beta".
NOT_STORY_MISSIONS = {4030001}

LANGUAGES = {"en": "EN", "zh-CN": "CHS"}

TAG = re.compile(r"<[^>]+>")


def load(path: Path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def version_key(version: str):
    return tuple(int(part) for part in version.split("."))


def build_gates(excel: Path):
    chapters = {row["ID"]: row for row in load(excel / "MissionChapterConfig.json")}
    text = load(excel.parent / "TextMap" / "TextMapEN.json")
    by_version = {}
    unmapped = set()
    for mission in load(excel / "MainMission.json"):
        if mission.get("Type") != "Main" or mission["MainMissionID"] in NOT_STORY_MISSIONS:
            continue
        chapter = mission.get("ChapterID", 0)
        if chapter in NOT_STORY_CHAPTERS:
            continue
        version = CHAPTER_VERSIONS.get(chapter)
        if version is None:
            unmapped.add(chapter)
            continue
        entry = by_version.setdefault(version, {"Version": version, "Chapters": [], "MainMissions": []})
        name = text.get(str(chapters.get(chapter, {}).get("ChapterName", {}).get("Hash", "")), str(chapter))
        if name not in entry["Chapters"]:
            entry["Chapters"].append(name)
        entry["MainMissions"].append(mission["MainMissionID"])

    if unmapped:
        sys.exit(f"Story chapters without a version in CHAPTER_VERSIONS: {sorted(unmapped)}")
    missing = sorted(set(CHAPTER_VERSIONS) - {m.get("ChapterID") for m in load(excel / "MainMission.json")})
    if missing:
        sys.exit(f"CHAPTER_VERSIONS lists chapters that are not in MainMission.json: {missing}")

    gates = sorted(by_version.values(), key=lambda entry: version_key(entry["Version"]))
    for entry in gates:
        entry["MainMissions"].sort()
    return {
        "Source": "MainMission.json chapters; versions from the Fandom wiki Trailblaze Mission change history "
                  "(scripts/generate-warp-data.py)",
        "Gates": gates,
    }


def build_names(excel: Path):
    rows = []
    for table, key, field in (("AvatarConfig", "AvatarID", "AvatarName"),
                              ("AvatarConfigLD", "AvatarID", "AvatarName"),
                              ("EquipmentConfig", "EquipmentID", "EquipmentName")):
        path = excel / f"{table}.json"
        if path.exists():
            rows += [(row[key], row.get(field, {}).get("Hash")) for row in load(path)]

    names = {}
    for lang, suffix in LANGUAGES.items():
        text = load(excel.parent / "TextMap" / f"TextMap{suffix}.json")
        table = {}
        for item_id, hash_value in rows:
            name = TAG.sub("", text.get(str(hash_value), "")).strip()
            if name and str(item_id) not in table:
                table[str(item_id)] = name
        names[lang] = dict(sorted(table.items(), key=lambda pair: int(pair[0])))
    return {
        "Source": "AvatarConfig(LD) / EquipmentConfig names through TextMapEN / TextMapCHS "
                  "(scripts/generate-warp-data.py)",
        "Names": names,
    }


def write(path: Path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("resources", type=Path, help="game data tree with ExcelOutput/ and TextMap/")
    parser.add_argument("--out", type=Path, default=Path("Config/Custom"))
    args = parser.parse_args()

    excel = args.resources / "ExcelOutput"
    if not excel.is_dir() or not (args.resources / "TextMap").is_dir():
        sys.exit(f"{args.resources} needs ExcelOutput/ and TextMap/")

    gates = build_gates(excel)
    names = build_names(excel)
    args.out.mkdir(parents=True, exist_ok=True)
    write(args.out / "WarpStoryGates.json", gates)
    write(args.out / "WarpItemNames.json", names)
    print(f"{len(gates['Gates'])} story versions, "
          + ", ".join(f"{lang}: {len(table)} names" for lang, table in names["Names"].items()))


if __name__ == "__main__":
    main()
