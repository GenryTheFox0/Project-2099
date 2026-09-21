"""GENRY V2 (2026-09-21, Shram): strip personal traces from a staged release root
or from payload/port. Removes internal QA notes and rewrites captured user paths.

Usage: python sanitize_release_root.py <root> [<root> ...]
"""
import os
import re
import sys

QA_NOTES = [
    'Support/ACCEPTED_FINAL_QTE_RU_20260909.md',
    'Support/ACHIEVEMENT_PREVIEW_FIX.json',
    'Support/AUDIO_WEAKPC_CANDIDATE_20260909.md',
    'Support/HUD_TEXT_ICON_FIX.json',
    'Support/QTE_TRANSLATION_FIX.json',
]
# A captured profile path: C:\Users\<name>\... -> %LOCALAPPDATA%
USER_PATH = re.compile(r'[A-Za-z]:[\\/]+Users[\\/]+[^\\/\r\n"]+', re.IGNORECASE)
SANITIZE = ['Support/DLSS5/ReShade.ini']


def main():
    for root in sys.argv[1:]:
        for rel in QA_NOTES:
            path = os.path.join(root, rel.replace('/', os.sep))
            if os.path.exists(path):
                os.remove(path)
                print('removed', path)
        for rel in SANITIZE:
            path = os.path.join(root, rel.replace('/', os.sep))
            if not os.path.exists(path):
                continue
            with open(path, encoding='utf-8', errors='replace') as handle:
                text = handle.read()
            clean = USER_PATH.sub('%LOCALAPPDATA%', text)
            if clean != text:
                with open(path, 'w', encoding='utf-8', newline='\r\n') as handle:
                    handle.write(clean)
                print('sanitized', path)
        left = []
        for base, _, files in os.walk(root):
            for name in files:
                if not name.lower().endswith(('.ini', '.json', '.txt', '.md', '.toml', '.xml', '.cfg')):
                    continue
                path = os.path.join(base, name)
                try:
                    with open(path, encoding='utf-8', errors='ignore') as handle:
                        body = handle.read()
                except OSError:
                    continue
                if USER_PATH.search(body):
                    left.append(os.path.relpath(path, root))
        print(root, '-> remaining files with a user path:', left or 'none')


if __name__ == '__main__':
    main()
