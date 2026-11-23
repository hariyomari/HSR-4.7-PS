#!/usr/bin/env python3
"""Exercise MSBuild generation in an isolated temporary checkout (.NET 10 + Python 3)."""
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    source = Path(__file__).resolve().parents[1] / 'Proto'
    with tempfile.TemporaryDirectory(prefix='honey protocol test ') as directory:
        project = Path(directory) / 'Proto'
        project.mkdir()
        shutil.copy2(source / 'Proto.csproj', project / 'Proto.csproj')
        shutil.copytree(source / 'Build', project / 'Build')
        # The project takes its SDK, shared defaults and package versions from the repository root.
        for name in ('global.json', 'Directory.Build.props', 'Directory.Packages.props'):
            shutil.copy2(source.parent / name, Path(directory) / name)
        # Keep this test scoped to the active version; never alter the real checkout.
        import xml.etree.ElementTree as ET
        version = ET.parse(source / 'Proto.csproj').findtext('.//ActiveProtocolVersion')
        schemas = project / version
        schemas.mkdir()
        for path in (source / version).glob('*.proto'):
            shutil.copy2(path, schemas / path.name)
        command = ['dotnet', 'msbuild', 'Proto.csproj', '-t:GenerateProtocolSources',
                   '-p:Configuration=Release', '-v:minimal', '-m:1']

        def generate(*args):
            # dotnet writes UTF-8 whatever the console code page is; the locale default breaks on localized output.
            result = subprocess.run(command + list(args), cwd=project, encoding='utf-8', errors='replace',
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            if result.returncode:
                raise RuntimeError(result.stdout)
            return result.stdout

        generate('-restore')
        output = project / 'obj' / 'Release' / 'net10.0' / 'protobuf' / version
        baseline = {p.name: p.read_bytes() for p in output.glob('*.cs')}
        assert len(baseline) == len(list(schemas.glob('*.proto')))
        stamp = output / 'generation.stamp'
        timestamp = stamp.stat().st_mtime_ns
        generate()
        assert stamp.stat().st_mtime_ns == timestamp, 'No-op build regenerated files'
        print('PASS: fresh generation in path with spaces and incremental skip')

        missing = output / next(iter(baseline))
        missing.unlink()
        generate()
        assert missing.read_bytes() == baseline[missing.name]
        print('PASS: missing generated output is repaired')

        probe = schemas / 'RepositorySlimmingProbe.proto'
        probe.write_text('syntax = "proto3"; package slimming_test; '
                         'message RepositorySlimmingProbe { int32 value = 1; }\n')
        generate()
        generated_probe = output / 'RepositorySlimmingProbe.cs'
        assert generated_probe.exists()
        probe.write_text(probe.read_text().replace('int32', 'int64'))
        generate()
        assert 'long value_' in generated_probe.read_text()
        probe.unlink()
        generate()
        assert not generated_probe.exists(), 'Removed schema left stale generated code'
        assert {p.name: p.read_bytes() for p in output.glob('*.cs')} == baseline
        print('PASS: schema addition, modification, and removal')

        # Cross-targeting must still invoke the compiler for the build host.
        generate('-p:RuntimeIdentifier=win-x64')
        cross = project / 'obj' / 'Release' / 'net10.0' / 'win-x64' / 'protobuf' / version
        assert {p.name: p.read_bytes() for p in cross.glob('*.cs')} == baseline
        print('PASS: target runtime does not change host compiler selection')


if __name__ == '__main__':
    main()
