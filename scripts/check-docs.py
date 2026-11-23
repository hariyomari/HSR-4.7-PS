#!/usr/bin/env python3
"""Check the documentation set for broken links and missing translations.

- Every relative link in a tracked Markdown file points at a file that exists.
- English is the authoritative language: `name.md`. A Simplified Chinese translation is
  `name.zh-CN.md` next to it and must have that English original.
- Other languages exist only for the project README, under docs/project-overview/.
- A document without a language suffix is written in English; tables and code are not counted.
- Every document under docs/ is listed in the index of its language (docs/README.md or
  docs/README.zh-CN.md).
"""
import re
import subprocess
import sys
from pathlib import Path

LINK = re.compile(r'\[[^\]]*\]\(([^)\s]+)\)')
TRANSLATION = re.compile(r'\.(zh-CN|zh-TW|ja-JP)\.md$')
README_TRANSLATIONS = 'docs/project-overview/'
CJK = re.compile('[' + chr(0x3400) + '-' + chr(0x9FFF) + ']')


def tracked_markdown():
    out = subprocess.run(['git', 'ls-files', '-z', '*.md'], stdout=subprocess.PIPE, check=True).stdout
    return [p for p in out.decode('utf-8').split('\0') if p and Path(p).exists()]


def links(path):
    text = Path(path).read_text(encoding='utf-8')
    text = re.sub(r'```.*?```', '', text, flags=re.S)  # commands and samples are not links
    for target in LINK.findall(text):
        if re.match(r'[a-z][a-z0-9+.-]*:', target) or target.startswith('#'):
            continue  # another site, a mail address, or an anchor in the same file
        yield target.split('#', 1)[0]


def main():
    files = tracked_markdown()
    problems = []

    for path in files:
        for target in links(path):
            if not (Path(path).parent / target).exists():
                problems.append(f'{path}: link to missing file {target}')

    for path in files:
        match = TRANSLATION.search(path)
        if not match:
            continue
        if path.startswith(README_TRANSLATIONS):
            continue
        if match.group(1) != 'zh-CN':
            problems.append(f'{path}: only the project README is kept in {match.group(1)}')
        elif not Path(TRANSLATION.sub('.md', path)).exists():
            problems.append(f'{path}: translation without an English original')

    for path in files:
        if TRANSLATION.search(path):
            continue
        prose = re.sub(r'```.*?```', '', Path(path).read_text(encoding='utf-8'), flags=re.S)
        prose = '\n'.join(line for line in prose.split('\n') if not line.lstrip().startswith('|'))
        prose = re.sub(r'`[^`]*`', '', prose)
        chinese_letters = len(CJK.findall(prose))
        if chinese_letters > 0.15 * max(len(re.findall(r'[A-Za-z]', prose)), 1):
            problems.append(f'{path}: the authoritative version of a document is written in English')

    for index, chinese in (('docs/README.md', False), ('docs/README.zh-CN.md', True)):
        if not Path(index).exists():
            problems.append(f'{index}: index is missing')
            continue
        listed = {(Path(index).parent / target).as_posix() for target in links(index)}
        for path in files:
            if not path.startswith('docs/') or path.startswith(README_TRANSLATIONS):
                continue
            if Path(path).name.startswith('README'):
                continue
            if path.endswith('.zh-CN.md') != chinese or (TRANSLATION.search(path) and not chinese):
                continue
            if path not in listed:
                problems.append(f'{index}: does not list {path}')

    for problem in sorted(set(problems)):
        print(problem)
    print(f'Documentation check: {len(set(problems))} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
