"""Prints the release page for a version: .github/release-template.md filled from docs/releases/<version>.md.

The notes file holds the Turkish text, then a "## English" heading and the English text. Run in GitHub Actions,
where GITHUB_REPOSITORY and GITHUB_SHA are set.
"""
import os
import pathlib
import re
import sys

version = sys.argv[1]
root = pathlib.Path(__file__).resolve().parent.parent
notes = (root / 'docs' / 'releases' / f'{version}.md').read_text(encoding='utf-8')
parts = re.split(r'^## English\s*$', notes, maxsplit=1, flags=re.MULTILINE)
if len(parts) != 2:
    sys.exit(f'docs/releases/{version}.md needs a "## English" section.')

repo = os.environ['GITHUB_REPOSITORY']
sha = os.environ['GITHUB_SHA']
values = {
    'version': version,
    'repo': repo,
    'sha': sha,
    'short': sha[:7],
    'download': f'https://github.com/{repo}/releases/download/v{version}',
    'raw': f'https://raw.githubusercontent.com/{repo}/{sha}',
    'changes': parts[0].strip(),
    'changes_en': parts[1].strip(),
}
page = (root / '.github' / 'release-template.md').read_text(encoding='utf-8')
page = re.sub(r'\{\{(\w+)\}\}', lambda m: values[m.group(1)], page)
sys.stdout.write(page)
