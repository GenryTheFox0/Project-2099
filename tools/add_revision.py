# -*- coding: utf-8 -*-
"""Teach the installer a new dump from the thirteen files that carry text.

When someone's dump cannot be translated, the installer writes their file
hashes into Support/Install/INSTALL_RECEIPT.json and installs in English. All
that is missing is one step: a delta from their text files to the canonical
English ones. The Russian stage after that is the same for everybody.

    python tools/add_revision.py --dump <folder> --name "romsfun ISO 2011"

<folder> needs only the files the translation touches -- the whole dump works
too. Nothing is downloaded and nothing is uploaded; deltas are written into
payload/patches and the index is updated in place.
"""
import argparse
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATCHES = os.path.join(ROOT, 'payload', 'patches')
INDEX = os.path.join(PATCHES, 'index.json')
BUILDER = os.path.join(ROOT, 'build', 'tools', 'BuildEotpPatches.exe')


def sha256(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as handle:
        for chunk in iter(lambda: handle.read(4 << 20), b''):
            digest.update(chunk)
    return digest.hexdigest().upper()


def load(path):
    return json.load(io.open(path, encoding='utf-8-sig'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dump', required=True, help='folder holding the dump (or just its text files)')
    parser.add_argument('--english', default=None, help='canonical English tree (Data/Original)')
    parser.add_argument('--name', default='added revision')
    parser.add_argument('--dry-run', action='store_true')
    options = parser.parse_args()

    english_root = options.english or os.environ.get('EOT_ORIGINAL_ROOT')
    if not english_root or not os.path.isfile(os.path.join(english_root, 'Default.xex')):
        raise SystemExit('нужна эталонная английская дорожка: --english <...>\\Data\\Original')
    if not os.path.isfile(BUILDER):
        raise SystemExit('нет %s — собери инструменты: powershell -File build_tools.ps1' % BUILDER)

    index = load(INDEX)
    english_known = set((e['Path'].lower(), e['SourceSha256'].upper()) for e in index['English'])
    canonical = {}
    for entry in index['Russian']:
        canonical[entry['Path'].lower()] = entry['SourceSha256'].upper()

    wanted = []
    for path in sorted(canonical):
        local = os.path.join(options.dump, path.replace('/', os.sep))
        if not os.path.isfile(local):
            raise SystemExit('в присланной папке нет %s' % path)
        digest = sha256(local)
        if digest == canonical[path]:
            print('%-44s уже канонический' % path)
            continue
        if (path, digest) in english_known:
            print('%-44s уже известен' % path)
            continue
        wanted.append((path, digest, os.path.getsize(local)))
        print('%-44s НОВЫЙ %s' % (path, digest[:12].lower()))

    if not wanted:
        print('\\nэтому образу ничего не нужно — он уже поддержан')
        return 0
    if options.dry_run:
        print('\\nсухой прогон: добавилось бы %d файлов' % len(wanted))
        return 0

    work = tempfile.mkdtemp(prefix='eot-revision-')
    try:
        manifest = {
            'Schema': 1, 'Id': 'added', 'Title': 'Spider-Man: Edge of Time',
            'TitleId': '415608B2', 'Region': options.name,
            'QuickChecks': [wanted[0][0]],
            'Files': [{'Path': p, 'Size': size, 'Sha256': digest} for p, digest, size in wanted],
        }
        manifest_path = os.path.join(work, 'manifest.json')
        io.open(manifest_path, 'w', encoding='utf-8').write(json.dumps(manifest, ensure_ascii=False))
        out = os.path.join(work, 'sets')
        os.makedirs(out)
        result = subprocess.call([BUILDER, manifest_path, options.dump, english_root, out,
                                  'english.json', 'New revision to clean Original'])
        if result:
            raise SystemExit('BuildEotpPatches вернул %d' % result)

        produced = load(os.path.join(out, 'english.json'))
        data_dir = os.path.join(PATCHES, 'data')
        os.path.isdir(data_dir) or os.makedirs(data_dir)
        added = 0
        for patch in produced['Files']:
            stored = 'data/' + patch['DeltaSha256'].lower() + '.eotp'
            target = os.path.join(PATCHES, stored.replace('/', os.sep))
            if not os.path.exists(target):
                shutil.copyfile(os.path.join(out, patch['Delta'].replace('/', os.sep)), target)
            index['English'].append({
                'Path': patch['Path'],
                'SourceSize': patch['SourceSize'], 'SourceSha256': patch['SourceSha256'],
                'TargetSize': patch['TargetSize'], 'TargetSha256': patch['TargetSha256'],
                'Delta': stored, 'DeltaSize': patch['DeltaSize'], 'DeltaSha256': patch['DeltaSha256'],
            })
            added += 1

        # Every English target must be a Russian source, or the second stage
        # would have nothing to apply and the dump would still install in English.
        for entry in index['English']:
            path = entry['Path'].lower()
            if path in canonical and entry['TargetSha256'].upper() != canonical[path]:
                raise SystemExit('цель для %s не совпала с канонической — индекс не тронут' % entry['Path'])

        index['English'].sort(key=lambda e: (e['Path'].lower(), e['SourceSha256']))
        io.open(INDEX, 'w', encoding='utf-8').write(json.dumps(index, ensure_ascii=False, separators=(',', ':')))
        print('\\nдобавлено записей: %d. Пересобери payload и залей:' % added)
        print('  powershell -File build_release.ps1 -Version vX.Y.Z')
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
