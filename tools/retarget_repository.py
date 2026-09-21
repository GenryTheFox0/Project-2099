"""GENRY V2 (2026-09-21, Shram): move every repository reference to a new
GitHub account/repository in one pass.

The old account was taken down, so every link built from it -- release URLs in
the installer channel, raw.githubusercontent image links in the notes, the
update sources the launcher reads, the publishing instructions -- points at
nothing. This rewrites them all and reports what changed, so nothing is left
silently pointing at a dead page.

Only github.com and raw.githubusercontent.com links are touched. Telegram and
the payment providers are separate services that do not move with the GitHub
account; rewriting external-service usernames by a blanket replacement has
already produced dead links once.

Usage: python retarget_repository.py <old owner/repo> <new owner/repo> [root]
"""
import os
import re
import sys

SKIP_DIRS = {'.git', 'payload', 'build', 'artifacts', 'licenses', '__pycache__'}
TEXT_SUFFIXES = {'.md', '.txt', '.json', '.ps1', '.py', '.cs', '.xml', '.toml', '.yml', '.yaml'}
GITHUB_HOSTS = ('github.com', 'raw.githubusercontent.com', 'api.github.com')


def rewrite(text, old, new):
    old_owner, new_owner = old.split('/')[0], new.split('/')[0]
    out = text.replace(old, new)
    # An owner mention that is part of a GitHub URL, e.g. github.com/<owner>/<other repo>.
    for host in GITHUB_HOSTS:
        out = re.sub(re.escape(host) + r'/' + re.escape(old_owner) + r'(?=[/"\s)\]]|$)',
                     host + '/' + new_owner, out)
    return out


def files_under(root):
    for base, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in names:
            if os.path.splitext(name)[1].lower() in TEXT_SUFFIXES:
                yield os.path.join(base, name)


def main():
    old, new = sys.argv[1], sys.argv[2]
    root = os.path.abspath(sys.argv[3]) if len(sys.argv) > 3 else os.path.abspath('.')
    changed, left = [], []
    for path in files_under(root):
        try:
            with open(path, encoding='utf-8') as handle:
                text = handle.read()
        except (OSError, UnicodeDecodeError):
            continue
        updated = rewrite(text, old, new)
        if updated != text:
            with open(path, 'w', encoding='utf-8', newline='') as handle:
                handle.write(updated)
            changed.append(os.path.relpath(path, root))
        if old in updated or re.search(r'github\.com/' + re.escape(old.split('/')[0]) + r'(?=[/"\s)\]]|$)', updated):
            left.append(os.path.relpath(path, root))
    print('files updated:', len(changed))
    for item in sorted(changed):
        print('  ', item)
    print('still referencing the old repository:', sorted(set(left)) or 'none')


if __name__ == '__main__':
    main()
