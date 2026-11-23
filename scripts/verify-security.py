#!/usr/bin/env python3
"""Run the resource-independent security gate; optionally require private-resource suites.

No server deployment, database import, or real account operations are performed.
Each regression owns isolated data. Resource-backed checks are opt-in and their
absence is reported as SKIP, never as a passing resource test.
"""
from pathlib import Path
import argparse
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def run(command):
    print('+ ' + ' '.join(command), flush=True)
    result = subprocess.run(command, cwd=ROOT)
    if result.returncode:
        raise RuntimeError(f'Command failed ({result.returncode}): {command}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--with-resources', action='store_true',
                        help='Require Config/Resource.bin and run LoginScene/GridFight/DailyActive suites')
    parser.add_argument('--skip-build', action='store_true', help='Use a previously verified solution build')
    args = parser.parse_args()
    run([sys.executable, 'scripts/check-comments.py'])
    run([sys.executable, 'scripts/check-docs.py'])
    version = ET.parse(ROOT / 'Proto/Proto.csproj').findtext('.//ActiveProtocolVersion')
    if (ROOT / 'KcpSharp/CmdIds.cs').read_bytes() != (ROOT / 'Proto' / version / 'CmdIds.cs').read_bytes():
        raise RuntimeError('Active protocol CmdIds.cs copies differ')
    print('PASS: active protocol command IDs match')
    if not args.skip_build:
        run(['dotnet', 'build', 'March7thHoney.sln', '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false'])
    for name in ['AuthRegression', 'WebServerRegression', 'KcpSecurityRegression', 'KcpAdmissionRegression',
                 'InventoryRegression', 'SourceGenRegression']:
        project = f'scripts/{name}/{name}.csproj'
        run(['dotnet', 'build', project, '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false'])
        run(['dotnet', 'run', '--project', project, '-c', 'Release', '--no-build', '--no-restore'])
    run([sys.executable, 'scripts/test-proto-generation.py'])
    if shutil.which("bash") and shutil.which("flock"):
        run([sys.executable, "scripts/test-update-server.py"])
    else:
        print("SKIP: Linux deployment mock suite (requires bash and flock)")
    if args.with_resources:
        if not (ROOT / 'Config/Resource.bin').is_file():
            raise RuntimeError('--with-resources requires an authentic, matching Config/Resource.bin')
        for name in ['LoginSceneRegression', 'GridFightRegression', 'DailyActiveRegression']:
            project = f'scripts/{name}/{name}.csproj'
            run(['dotnet', 'build', project, '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false'])
            run(['dotnet', 'run', '--project', project, '-c', 'Release', '--no-build', '--no-restore'])
    else:
        print('SKIP: LoginScene/GridFight/DailyActive resource suites (use --with-resources)')
    print('PASS: requested security gate; skipped resource suites are NOT validated')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        sys.exit(1)
