#!/usr/bin/env python3
"""Check text encoding and the language of new comments.

Every tracked text file must be valid UTF-8 and free of replacement characters, the mark a wrong
encoding leaves behind. With --base <ref>, C# lines added since that ref must not introduce a
full-line comment written in Chinese. A game term may still be given in Chinese in parentheses or
quotes after its English name, and a trailing comment that labels a data entry (an ID with the name
the game gives it) is allowed.
"""
import argparse
import re
import subprocess
import sys

TEXT_EXTENSIONS = ('.cs', '.csproj', '.props', '.targets', '.sln', '.md', '.json', '.txt', '.proto',
                   '.py', '.sh', '.command', '.bat', '.yml', '.yaml', '.editorconfig', '.gitignore',
                   '.gitattributes')
CJK = re.compile('[' + chr(0x3400) + '-' + chr(0x9FFF) + ']')
# A game term may be given in Chinese once, in parentheses or quotes after its English name.
GLOSS = re.compile(r'\([^()]*\)|（[^（）]*）|"[^"]*"|“[^”]*”|「[^」]*」|`[^`]*`')


def is_chinese(comment):
    """True when Chinese text remains outside parenthesized or quoted terms."""
    return bool(CJK.search(GLOSS.sub('', comment)))


def git(*args):
    result = subprocess.run(['git', *args], stdout=subprocess.PIPE, check=True)
    return result.stdout.decode('utf-8', errors='replace')


def encoding_problems():
    problems = []
    for path in git('ls-files', '-z').split('\0'):
        if not path.endswith(TEXT_EXTENSIONS):
            continue
        try:
            with open(path, 'rb') as file:
                text = file.read().decode('utf-8')
        except FileNotFoundError:
            continue  # deleted in the working tree
        except UnicodeDecodeError as error:
            problems.append(f'{path}: not valid UTF-8 ({error.reason} at byte {error.start})')
            continue
        for number, line in enumerate(text.split('\n'), 1):
            if chr(0xFFFD) in line:
                problems.append(f'{path}:{number}: replacement character, the text was saved with the wrong encoding')
    return problems


def comment_problems(base):
    problems = []
    path = None
    number = 0
    for line in git('diff', '--unified=0', '--no-color', base, 'HEAD', '--', '*.cs').split('\n'):
        if line.startswith('+++ '):
            path = line[6:] if line.startswith('+++ b/') else None
        elif line.startswith('@@'):
            number = int(re.search(r'\+(\d+)', line).group(1))
        elif line.startswith('+') and path:
            added = line[1:].strip()
            if added.startswith('//') and is_chinese(added):
                problems.append(f'{path}:{number}: write comments in English')
            number += 1
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', help='also check C# lines added since this commit')
    args = parser.parse_args()

    problems = encoding_problems()
    if args.base:
        problems += comment_problems(args.base)
    for problem in problems:
        print(problem)
    print(f'Comment and encoding check: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
