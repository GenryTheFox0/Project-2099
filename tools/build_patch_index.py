# -*- coding: utf-8 -*-
"""Preserve ALL normalization deltas, including embedded level subtitles.

A small Russian patch set is not an inventory of text-bearing packages.
Historical 'Original' targets can themselves be contaminated: full coverage
alone is not proof that a package is safe to release.
"""
import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
VARIANTS = ('eu-retail', 'usa-europe-retail', 'usa-europe-retail-r2',
            'ru-god-alt', 'sazanoff-rus-god')
MANIFEST_NAMES = dict(zip(VARIANTS, ('eu', 'usa-europe', 'usa-europe-r2', 'ru-god', 'sazanoff')))


def load(path):
    with open(path, encoding='utf-8-sig') as handle:
        return json.load(handle)


def key(path, digest):
    return path.replace('\\', '/').lower(), digest.upper()


def targets(manifest):
    result = {}
    for item in manifest['Files']:
        path = item['Path'].replace('\\', '/').lower()
        if path in result:
            raise ValueError('Duplicate canonical path: ' + path)
        result[path] = item['Sha256'].upper(), item['Size']
    return result


def validate_coverage(index, sets):
    """Missing a known level normalization must fail, not claim completeness."""
    lookup = {key(e['Path'], e['SourceSha256']): e for e in index['English']}
    for variant, lanes in sets.items():
        for patch in lanes['original']:
            found = lookup.get(key(patch['Path'], patch['SourceSha256']))
            if found is None or (found['TargetSha256'].upper(), found['TargetSize']) != (
                    patch['TargetSha256'].upper(), patch['TargetSize']):
                raise ValueError('Missing normalization: %s / %s' % (variant, patch['Path']))


def route(lookup, path, digest, size):
    visited = set()
    while True:
        identity = key(path, digest)
        patch = lookup.get(identity)
        if patch is None:
            return digest.upper(), size
        if identity in visited or len(visited) >= 16:
            raise ValueError('Cyclic or excessive patch chain: ' + path)
        visited.add(identity)
        if patch['SourceSize'] != size:
            raise ValueError('Patch-chain source size mismatch: ' + path)
        digest, size = patch['TargetSha256'].upper(), patch['TargetSize']


def build_index(sets, expected_original=None, expected_russian=None, source_manifests=None,
                corrections=None):
    if bool(expected_original) != bool(expected_russian):
        raise ValueError('Both canonical manifests are required')
    index = {'Schema': 4, 'CanonicalTargetsVerified': False, 'English': [], 'Russian': []}
    owners, seen = {}, {}
    for variant, lanes in sets.items():
        # No filename whitelist: level packages also carry dialogue.
        for patch in lanes['original']:
            identity = key(patch['Path'], patch['SourceSha256'])
            if identity in seen:
                old = seen[identity]
                if (old['TargetSha256'].upper(), old['TargetSize']) != (
                        patch['TargetSha256'].upper(), patch['TargetSize']):
                    raise ValueError('Conflicting normalization target: ' + patch['Path'])
                continue
            seen[identity] = patch
            index['English'].append(dict(patch))
            owners[('English', identity)] = variant, patch['Delta']
    for patch in sets['eu-retail']['russian']:
        identity = key(patch['Path'], patch['SourceSha256'])
        if ('Russian', identity) in owners:
            raise ValueError('Duplicate Russian transformation: ' + patch['Path'])
        index['Russian'].append(dict(patch))
        owners[('Russian', identity)] = 'eu-retail', patch['Delta']
    for stage, bundle in (corrections or {}).items():
        if stage not in ('English', 'Russian'):
            raise ValueError('Unknown correction stage: ' + stage)
        for patch in bundle['Files']:
            identity = key(patch['Path'], patch['SourceSha256'])
            if (stage, identity) in owners:
                old = next(e for e in index[stage]
                           if key(e['Path'], e['SourceSha256']) == identity)
                if (old['SourceSize'], old['TargetSha256'].upper(), old['TargetSize']) != (
                        patch['SourceSize'], patch['TargetSha256'].upper(), patch['TargetSize']):
                    raise ValueError('Conflicting corrective transformation: ' + patch['Path'])
                # Rebuilding per-image sets from an already repaired Original
                # tree can also produce this exact correction. Store it once.
                continue
            index[stage].append(dict(patch))
            owners[(stage, identity)] = bundle['Root'], patch['Delta']
    validate_coverage(index, sets)
    english_lookup = {key(e['Path'], e['SourceSha256']): e for e in index['English']}
    russian_lookup = {key(e['Path'], e['SourceSha256']): e for e in index['Russian']}
    for stage, lookup in (('English', english_lookup), ('Russian', russian_lookup)):
        for patch in index[stage]:
            route(lookup, patch['Path'], patch['SourceSha256'], patch['SourceSize'])
    canonical = {e['Path'].lower(): e['SourceSha256'].upper() for e in index['Russian']}
    for patch in index['English']:
        wanted = canonical.get(patch['Path'].lower())
        final_hash, _ = route(english_lookup, patch['Path'], patch['SourceSha256'], patch['SourceSize'])
        if wanted and wanted != final_hash:
            raise ValueError('English target is not a Russian-stage source: ' + patch['Path'])

    if expected_original:
        original, russian = targets(expected_original), targets(expected_russian)
        for stage, expected in (('English', original), ('Russian', russian)):
            for patch in index[stage]:
                lookup = english_lookup if stage == 'English' else russian_lookup
                if expected.get(patch['Path'].lower()) != route(
                        lookup, patch['Path'], patch['SourceSha256'], patch['SourceSize']):
                    raise ValueError('Unverified %s target: %s' % (stage, patch['Path']))
        if not source_manifests or set(source_manifests) != set(sets):
            raise ValueError('A source manifest for every variant is required')
        for variant, manifest in source_manifests.items():
            source = targets(manifest)
            for path, (digest, size) in source.items():
                digest, size = route(english_lookup, path, digest, size)
                if original.get(path) != (digest, size):
                    raise ValueError('Unverified Original passthrough: %s / %s' % (variant, path))
                digest, size = route(russian_lookup, path, digest, size)
                if russian.get(path) != (digest, size):
                    raise ValueError('Unverified Russian passthrough: %s / %s' % (variant, path))
            if set(source) != set(original) or set(original) != set(russian):
                raise ValueError('Canonical/source file inventory differs: ' + variant)
        index['CanonicalTargetsVerified'] = True
        index['CanonicalOriginal'] = expected_original['Files']
        index['CanonicalRussian'] = expected_russian['Files']
    for stage in ('English', 'Russian'):
        index[stage].sort(key=lambda e: key(e['Path'], e['SourceSha256']))
    return index, owners


def store_index(index, owners, sets_root, output, index_only=False):
    output.mkdir(parents=True, exist_ok=True)
    # Do not erase the pool before validation, nor trust existing filenames.
    for stage in ('English', 'Russian'):
        for patch in index[stage]:
            variant, relative = owners[(stage, key(patch['Path'], patch['SourceSha256']))]
            source = sets_root / variant / relative
            stored = 'data/' + patch['DeltaSha256'].lower() + '.eotp'
            if not index_only:
                destination = output / stored
                check = destination if destination.exists() else source
                if check.stat().st_size != patch['DeltaSize']:
                    raise ValueError('Delta size mismatch: ' + str(check))
                digest = hashlib.sha256()
                with check.open('rb') as handle:
                    for chunk in iter(lambda: handle.read(4 << 20), b''):
                        digest.update(chunk)
                if digest.hexdigest().upper() != patch['DeltaSha256'].upper():
                    raise ValueError('Delta hash mismatch: ' + str(check))
                if check == source:
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(source, destination)
            patch['Delta'] = stored
    if index_only:
        index['IndexOnlyNotInstallable'] = True
    temporary = output / 'index.json.pending'
    temporary.write_text(json.dumps(index, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    os.replace(str(temporary), str(output / 'index.json'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=BASE)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--expected-original', type=Path)
    parser.add_argument('--expected-russian', type=Path)
    parser.add_argument('--original-corrections', type=Path,
                        help='Extra historical-target to corrected-Original deltas')
    parser.add_argument('--russian-corrections', type=Path,
                        help='Corrected-Original to preserved-Russian deltas')
    parser.add_argument('--allow-unverified-targets', action='store_true',
                        help='QA only: old target hashes may encode contaminated text')
    parser.add_argument('--index-only', action='store_true', help='QA report, not installable')
    options = parser.parse_args()
    live = (options.root / 'payload/patches').resolve()
    output = (options.output or live).resolve()
    if not options.expected_original or not options.expected_russian:
        if not options.allow_unverified_targets or output == live:
            parser.error('Supply independently verified --expected-original and --expected-russian; '
                         'unverified QA output must use a separate --output directory')
    if options.index_only and output == live:
        parser.error('--index-only must use an isolated --output directory')
    sets_root = options.root / 'patchsets'
    sets = {v: {lane: load(sets_root / v / (lane + '.json'))['Files']
                for lane in ('original', 'russian')} for v in VARIANTS}
    sources = {v: load(options.root / 'manifests' /
                      ('source-manifest-' + MANIFEST_NAMES[v] + '.json')) for v in VARIANTS}
    corrections = {}
    for stage, path in (('English', options.original_corrections), ('Russian', options.russian_corrections)):
        if path:
            corrections[stage] = load(path)
            corrections[stage]['Root'] = str(path.resolve().parent)
    index, owners = build_index(sets,
        expected_original=load(options.expected_original) if options.expected_original else None,
        expected_russian=load(options.expected_russian) if options.expected_russian else None,
        source_manifests=sources, corrections=corrections)
    store_index(index, owners, sets_root, output, options.index_only)
    print('English: %d; Russian: %d; canonical targets verified: %s' %
          (len(index['English']), len(index['Russian']), index['CanonicalTargetsVerified']))
    print('WROTE ' + str(output / 'index.json'))


if __name__ == '__main__':
    main()
