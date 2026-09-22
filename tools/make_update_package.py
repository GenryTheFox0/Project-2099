# -*- coding: utf-8 -*-
"""Point the launcher's update sources at the live repository and build the
first changed-file package.

The machinery was already finished: staging, SHA-256 checks, backups, atomic
replace and rollback. It was dark for two reasons only -- every source carried
"Enabled": false, and the URLs named a repository that does not exist. Between
BETA 2 and BETA 3 exactly three files moved, so an update is ~17 MB instead of
the 917 MB payload.
"""
import hashlib
import io
import json
import os
import zipfile

CAND = 'E:/SpiderManEOT_Port_GENRY/_CANDIDATE_V100_SETTINGS_MODS_20260919'
PORT = 'D:/EOT_PC_FEATURES_20260905/EOTGitHubInstaller/payload/port'
OUT = 'D:/EOT_PC_FEATURES_20260905/EOTGitHubInstaller/artifacts/update-v2.0.0-beta.3'
REPO = 'https://github.com/GenryTheFox0/Project-2099'
TAG = 'update-files-v2.0.0-beta.3'   # служебный пререлиз, чтобы не засорять страницу релиза
GENERATION = 20304          # above MinimumGeneration 10303; one step per release
RELEASE = 'v2.0.0-beta.3'
PUBLISHED = '2026-09-22T12:00:00Z'
NOTES = 'Паутина вместо строки состояния, живые источники обновлений, DPI-осознанность порта'

# What actually changed between BETA 2 and BETA 3.
CHANGED = ['SpiderManEOT.exe', 'rexruntime.dll', 'Launcher.exe', 'Launcher.UpdateHelper.exe',
           'runtime.manifest.json', 'update.sources.json']


def sha256(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as handle:
        for chunk in iter(lambda: handle.read(4 << 20), b''):
            digest.update(chunk)
    return digest.hexdigest().upper()


def sources_document():
    return {
        'Format': 'genry.eot.update-sources',
        'Schema': 1,
        'Channel': 'stable',
        'MinimumGeneration': 10303,
        'Sources': [
            {
                'Name': 'GitHub',
                'Kind': 'github',
                'Enabled': True,
                'ManifestUrl': REPO + '/releases/latest/download/EOT.update-manifest.json',
                'ContentBaseUrl': REPO + '/releases/download/',
            },
            {
                'Name': 'GitLab',
                'Kind': 'gitlab',
                'Enabled': False,
                'ManifestUrl': '',
                'ContentBaseUrl': '',
            },
            {
                'Name': 'Static CDN',
                'Kind': 'static-cdn',
                'Enabled': False,
                'ManifestUrl': '',
                'ContentBaseUrl': '',
            },
        ],
    }


def write_json(path, document):
    io.open(path, 'w', encoding='utf-8', newline='\n').write(
        json.dumps(document, ensure_ascii=False, indent=2) + '\n')


def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    # the source list is already live in both trees; keep it byte-identical
    for root in (CAND, PORT):
        write_json(os.path.join(root, 'update.sources.json'), sources_document())

    # 2. the network manifest: every file carries its own https mirror
    files = []
    for name in CHANGED:
        local = os.path.join(CAND, name)
        if not os.path.isfile(local):
            raise SystemExit('нет файла ' + local)
        files.append({
            'Path': name,
            'Size': os.path.getsize(local),
            'SHA256': sha256(local),
            'Mirrors': ['%s/releases/download/%s/%s' % (REPO, TAG, name)],
        })
    manifest = {
        'Format': 'genry.eot.update-manifest',
        'Schema': 1,
        'Channel': 'stable',
        'Generation': GENERATION,
        'Release': RELEASE,
        'PublishedUtc': PUBLISHED,
        'Notes': NOTES,
        'Files': files,
    }
    write_json(os.path.join(OUT, 'EOT.update-manifest.json'), manifest)

    # 3. the same thing as an offline package: no network, imported from the Project page
    local_manifest = json.loads(json.dumps(manifest))
    for entry in local_manifest['Files']:
        entry['Mirrors'] = []
    package = os.path.join(OUT, 'EOT-Update-%s.eotupdate' % RELEASE)
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as zip_file:
        zip_file.writestr('EOT.update-manifest.json',
                          json.dumps(local_manifest, ensure_ascii=False, indent=2) + '\n')
        for name in CHANGED:
            zip_file.write(os.path.join(CAND, name), name)

    total = sum(entry['Size'] for entry in files)
    print()
    print('пакет обновления: %.1f МБ против 917 МБ полной сборки' % (total / 1048576.0))
    for entry in files:
        print('   %-28s %8.1f МБ  %s' % (entry['Path'], entry['Size'] / 1048576.0, entry['SHA256'][:12]))
    print()
    print('оффлайн-пакет:', package, '%.1f МБ' % (os.path.getsize(package) / 1048576.0))
    print('сетевой манифест:', os.path.join(OUT, 'EOT.update-manifest.json'))


if __name__ == '__main__':
    main()
