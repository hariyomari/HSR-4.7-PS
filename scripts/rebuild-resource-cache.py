#!/usr/bin/env python3
"""Rebuild Config/Resource.bin via ResourceCacheFill with safe backup and rollback.

Wraps scripts/ResourceCacheFill so operators do not have to juggle temporary
output paths, manual backups, or shell-specific quoting. The base cache is never
overwritten in place; Config/Resource.bin is only replaced when --replace is
passed and the build (plus optional --verify regressions) exits cleanly.
"""
from datetime import datetime
from pathlib import Path
import argparse
import os
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
FILL_PROJECT = ROOT / "scripts/ResourceCacheFill/ResourceCacheFill.csproj"
REGRESSION_PROJECTS = [
    ("GridFightRegression", "--cache"),
    ("LoginSceneRegression", "--resource-cache"),
    ("DailyActiveRegression", "--cache"),
]


def run(command: list[str]) -> None:
    print("+ " + " ".join(command), flush=True)
    result = subprocess.run(command, cwd=ROOT)
    if result.returncode != 0:
        raise RuntimeError(f"Command failed ({result.returncode}): {' '.join(command)}")


def resolve_resources(raw: str | None) -> Path:
    if raw:
        path = Path(raw).expanduser().resolve()
        if not path.is_dir():
            raise FileNotFoundError(f"Resource directory not found: {path}")
        return path

    for candidate in (ROOT / "TurnBasedGameData", ROOT / "Resources"):
        if candidate.is_dir():
            return candidate.resolve()

    raise FileNotFoundError(
        "No resource directory specified and neither TurnBasedGameData/ nor Resources/ exists at the repo root."
    )


def safe_replace(new_cache: Path, target: Path, backup: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        if backup.exists():
            raise FileExistsError(f"Backup path already exists: {backup}")
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(target, backup)
        if backup.stat().st_size != target.stat().st_size:
            raise RuntimeError(f"Backup size mismatch for {backup}")
        print(f"Backed up existing cache to: {backup}", flush=True)

    staging = target.with_name(f".{target.name}.staging-{os.getpid()}")
    try:
        shutil.copy2(new_cache, staging)
        os.replace(staging, target)
    except BaseException:
        if staging.exists():
            staging.unlink()
        raise

    print(f"Replaced cache: {target}", flush=True)
    if backup.exists():
        print(f"Rollback:       cp \"{backup}\" \"{target}\"", flush=True)


def run_regressions(cache_path: Path, configuration: str, resource_version: str | None) -> None:
    for name, cache_flag in REGRESSION_PROJECTS:
        project = ROOT / f"scripts/{name}/{name}.csproj"
        cmd = ["dotnet", "run", "--project", str(project), "-c", configuration, "--", cache_flag, str(cache_path)]
        if name == "LoginSceneRegression" and resource_version:
            cmd.extend(["--resource-version", resource_version])
        run(cmd)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "resources",
        nargs="?",
        help="Path to raw resources (defaults to TurnBasedGameData/ or Resources/ in the repo root)",
    )
    parser.add_argument(
        "--base",
        default=str(ROOT / "Config/Resource.bin"),
        help="Existing base cache to fill gaps from (default: Config/Resource.bin)",
    )
    parser.add_argument(
        "--output",
        help="Path for the new cache file (default: artifacts/cache-rebuild-<timestamp>/Resource.new.bin)",
    )
    parser.add_argument(
        "--report-dir",
        help="Directory for ResourceCacheFill logs and fill-report.txt (default: next to --output)",
    )
    parser.add_argument(
        "--configuration",
        "-c",
        default="Release",
        choices=("Debug", "Release"),
        help="dotnet build configuration (default: Release)",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Run GridFightRegression, LoginSceneRegression, and DailyActiveRegression against the new cache",
    )
    parser.add_argument(
        "--resource-version",
        help="Forwarded to LoginSceneRegression --resource-version when --verify is set",
    )
    parser.add_argument(
        "--replace",
        action="store_true",
        help="Back up --target and atomically replace it with the new cache once all steps succeed",
    )
    parser.add_argument(
        "--target",
        default=str(ROOT / "Config/Resource.bin"),
        help="Cache file to back up and replace when --replace is set (default: Config/Resource.bin)",
    )
    parser.add_argument(
        "--backup",
        help="Backup path when --replace is set (default: <target>.backup-<timestamp>.bin)",
    )
    args = parser.parse_args()

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    resources = resolve_resources(args.resources)
    base_cache = Path(args.base).expanduser().resolve()
    if not base_cache.is_file():
        raise FileNotFoundError(f"Base cache not found: {base_cache}")

    default_work_dir = ROOT / "artifacts" / f"cache-rebuild-{timestamp}"
    output_cache = Path(args.output).expanduser().resolve() if args.output else (default_work_dir / "Resource.new.bin")
    report_dir = Path(args.report_dir).expanduser().resolve() if args.report_dir else (output_cache.parent / "fill-report")
    if output_cache.exists():
        raise FileExistsError(f"Output file already exists: {output_cache}")
    if output_cache == base_cache:
        raise ValueError("Output cache must be a different path from --base")

    run(
        [
            "dotnet",
            "run",
            "--project",
            str(FILL_PROJECT),
            "-c",
            args.configuration,
            "--",
            str(resources),
            str(base_cache),
            str(output_cache),
            str(report_dir),
        ]
    )

    if args.verify:
        run_regressions(output_cache, args.configuration, args.resource_version)

    target_cache = Path(args.target).expanduser().resolve()
    if args.replace:
        backup_cache = (
            Path(args.backup).expanduser().resolve()
            if args.backup
            else target_cache.with_name(f"{target_cache.name}.backup-{timestamp}.bin")
        )
        safe_replace(output_cache, target_cache, backup_cache)
    else:
        print(f"\nNew cache written to: {output_cache}")
        print(f"Fill report:          {report_dir / 'fill-report.txt'}")
        print("Re-run with --replace (and optionally --verify) to back up and swap in Config/Resource.bin.")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
