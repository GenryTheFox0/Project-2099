# -*- coding: utf-8 -*-
"""Turn the per-image patch sets into one index keyed by file hash.

A delta only ever says "a file with THIS sha256 becomes THAT one". Tying a whole
dump to a named revision was the mistake: every new dump needed its own set, and
a dump that differed only in level packages was refused outright.

Two observations collapse the whole thing:

  * only thirteen files carry text. Every other difference between dumps is a
    level package, and one revision's level package is as good as another's --
    it is the same level. Those deltas are pure weight.
  * the translation is one piece of work, not one per dump. Bring a dump's text
    files to the canonical English first, and a single Russian set then serves
    every dump that exists.

So the index has two stages: English (anything -> canonical English text) and
Russian (canonical English -> the translation), each keyed by source hash, with
every delta stored once by content.
"""
import io
import json
import os
import shutil

BASE = 'D:/EOT_PC_FEATURES_20260905/EOTGitHubInstaller'
SETS = os.path.join(BASE, 'patchsets')
PATCHES = os.path.join(BASE, 'payload/patches')
OUT_DATA = os.path.join(PATCHES, 'data')
VARIANTS = ['eu-retail', 'usa-europe-retail', 'usa-europe-retail-r2', 'ru-god-alt', 'sazanoff-rus-god']


def load(path):
    return json.load(io.open(path, encoding='utf-8-sig'))


def store(patch, variant, kept):
    """Copy a delta into the content-addressed pool; return its stored name."""
    stored = 'data/' + patch['DeltaSha256'].lower() + '.eotp'
    target = os.path.join(PATCHES, stored.replace('/', os.sep))
    if not os.path.exists(target):
        if not os.path.isdir(OUT_DATA):
            os.makedirs(OUT_DATA)
        shutil.copyfile(os.path.join(SETS, variant, patch['Delta'].replace('/', os.sep)), target)
        kept[0] += patch['DeltaSize']
    return stored


def entry(patch, stored):
    return {
        'Path': patch['Path'],
        'SourceSize': patch['SourceSize'], 'SourceSha256': patch['SourceSha256'],
        'TargetSize': patch['TargetSize'], 'TargetSha256': patch['TargetSha256'],
        'Delta': stored, 'DeltaSize': patch['DeltaSize'], 'DeltaSha256': patch['DeltaSha256'],
    }


def main():
    # eu-retail needs no normalisation at all, so its own files define canonical
    # English and its Russian set defines what the translation touches.
    russian_base = load(os.path.join(SETS, 'eu-retail/russian.json'))['Files']
    text_paths = set(f['Path'].lower() for f in russian_base)
    assert not load(os.path.join(SETS, 'eu-retail/original.json'))['Files'], 'eu-retail is not canonical'
    print('текстовых файлов: %d' % len(text_paths))

    kept = [0]
    index = {'Schema': 3, 'English': [], 'Russian': []}
    seen = {}
    dropped = dropped_bytes = 0

    for variant in VARIANTS:
        for patch in load(os.path.join(SETS, variant, 'original.json'))['Files']:
            path = patch['Path'].lower()
            if path not in text_paths and path != 'default.xex':
                dropped += 1
                dropped_bytes += patch['DeltaSize']
                continue
            key = ('English', path, patch['SourceSha256'].lower())
            if key in seen:
                assert seen[key] == patch['TargetSha256'].lower(), key
                continue
            seen[key] = patch['TargetSha256'].lower()
            index['English'].append(entry(patch, store(patch, variant, kept)))
        # every other variant's Russian set is the same translation reached from
        # a different starting point -- the English stage already covers that.
        for patch in load(os.path.join(SETS, variant, 'russian.json'))['Files']:
            if variant != 'eu-retail':
                dropped += 1
                dropped_bytes += patch['DeltaSize']
                continue
            index['Russian'].append(entry(patch, store(patch, variant, kept)))

    canonical = dict((e['Path'].lower(), e['SourceSha256'].lower()) for e in index['Russian'])
    for item in index['English']:
        path = item['Path'].lower()
        if path in canonical and item['TargetSha256'].lower() != canonical[path]:
            raise SystemExit('английская цель для %s не канонична' % item['Path'])

    for stage in ('English', 'Russian'):
        index[stage].sort(key=lambda e: (e['Path'].lower(), e['SourceSha256']))
        print('%-8s записей: %2d' % (stage, len(index[stage])))
    print('отброшено (нормализация уровней и лишние русские наборы): %d, %.1f МБ'
          % (dropped, dropped_bytes / 1048576.0))
    print('уникальных дельт: %.1f МБ' % (kept[0] / 1048576.0))

    # which dumps are fully covered, purely as a report
    for variant in VARIANTS:
        manifest = load(os.path.join(BASE, 'manifests/source-manifest-%s.json' % {
            'eu-retail': 'eu', 'usa-europe-retail': 'usa-europe', 'usa-europe-retail-r2': 'usa-europe-r2',
            'ru-god-alt': 'ru-god', 'sazanoff-rus-god': 'sazanoff'}[variant]))
        have = dict((f['Path'].lower(), f['Sha256'].lower()) for f in manifest['Files'])
        english = dict(((e['Path'].lower(), e['SourceSha256'].lower()), e['TargetSha256'].lower())
                       for e in index['English'])
        russian = set((e['Path'].lower(), e['SourceSha256'].lower()) for e in index['Russian'])
        ok = 0
        for path in sorted(text_paths):
            sha = have.get(path)
            sha = english.get((path, sha), sha)
            if (path, sha) in russian:
                ok += 1
        print('  %-22s русский текст собирается для %2d из %d файлов' % (variant, ok, len(text_paths)))

    io.open(os.path.join(PATCHES, 'index.json'), 'w', encoding='utf-8').write(
        json.dumps(index, ensure_ascii=False, separators=(',', ':')))
    print('написан index.json')


if __name__ == '__main__':
    main()
