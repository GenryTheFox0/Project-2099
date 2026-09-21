"""GENRY V2 (2026-09-21, Shram): build a clean release root for prepare_payload.ps1
from the live candidate folder.

The candidate is a development folder: it carries the whole game data, active mod
overlays (hardlinks to game data), backups, reports, test profiles and the
author's personal spider_man_edge_of_time.toml. None of that may reach the public
payload. This script copies only what ships and normalizes the config.

Usage: python stage_release_root.py <candidate> <staging>
"""
import json
import os
import re
import shutil
import subprocess
import sys

EXCLUDED_DIRS = {
    'Data/Original', 'Data/Russian',          # game data: the user brings their own copy
    'Mods', 'ModsTemplate',                   # mod content = derived game assets; the launcher recreates the folders
    'Reports', 'Screenshots', 'UserData', 'tests',
    'TestProfile', 'TestDLCProfile20260920',
    'Support/Backups', 'Support/Revisions', 'Support/Build', 'Support/Evidence',
    'Support/PhotoBuildBackups', 'Support/V2BrandBackups',
    'Cache/shaders/local',                    # machine-local D3D12 PSO library (93 MB), rebuilt per PC
}
EXCLUDED_DIR_PATTERNS = [re.compile(r'^Support/RetiredRootFiles_'), re.compile(r'^Mods/'), re.compile(r'^Tools/__pycache__')]
EXCLUDED_FILES = {'Launcher.Tests.exe',
                  # internal QA records: they carry the author's own paths and mean nothing to a player
                  'ACCEPTED_FINAL_QTE_RU_20260909.md', 'ACHIEVEMENT_PREVIEW_FIX.json',
                  'AUDIO_WEAKPC_CANDIDATE_20260909.md', 'HUD_TEXT_ICON_FIX.json',
                  'QTE_TRANSLATION_FIX.json'}
EXCLUDED_FILE_PATTERNS = [re.compile(r'\.log$'), re.compile(r'\.previous$'), re.compile(r'\.toml\.'), re.compile(r'\.before_'),
                          re.compile(r'\.pyc$'), re.compile(r'^launcher_settings\.json$')]

# The shipped config = candidate config with the author's personal choices reset.
TOML_OVERRIDES = {
    'eot_fov_scale': '1.0',
    'eot_internal_scale': '1',
    'eot_unlock_all_costumes': 'false',
    'show_fps_counter': 'false',
    'anisotropic_override': '5',
}
TOML_DROP = {'d3d12_pipeline_creation_threads'}   # the host picks the thread count per CPU


def excluded(rel, is_dir):
    rel = rel.replace('\\', '/')
    if is_dir:
        if rel in EXCLUDED_DIRS: return True
        return any(p.search(rel) for p in EXCLUDED_DIR_PATTERNS)
    name = rel.rsplit('/', 1)[-1]
    if name in EXCLUDED_FILES: return True
    return any(p.search(name) for p in EXCLUDED_FILE_PATTERNS)


def normalize_toml(text):
    out = []
    for line in text.splitlines():
        m = re.match(r'^\s*([A-Za-z0-9_]+)\s*=', line)
        if m:
            key = m.group(1)
            if key in TOML_DROP: continue
            if key in TOML_OVERRIDES:
                line = f'{key} = {TOML_OVERRIDES[key]}'
        out.append(line)
    return '\n'.join(out) + '\n'


def main():
    src, dst = map(os.path.abspath, sys.argv[1:3])
    if os.path.exists(dst):
        sys.exit(f'staging exists: {dst}')
    copied = 0; skipped = []
    for root, dirs, files in os.walk(src):
        rel_root = os.path.relpath(root, src)
        rel_root = '' if rel_root == '.' else rel_root
        keep = []
        for d in dirs:
            rel = os.path.join(rel_root, d) if rel_root else d
            if excluded(rel, True): skipped.append(rel + '/')
            else: keep.append(d)
        dirs[:] = keep
        for f in files:
            rel = os.path.join(rel_root, f) if rel_root else f
            if excluded(rel, False): skipped.append(rel); continue
            target = os.path.join(dst, rel)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if rel == 'spider_man_edge_of_time.toml':
                with open(os.path.join(root, f), encoding='utf-8') as h: text = h.read()
                with open(target, 'w', encoding='utf-8', newline='\n') as h: h.write(normalize_toml(text))
            else:
                shutil.copy2(os.path.join(root, f), target)
            copied += 1
    # Empty mod folders so the launcher and ModManager find their layout.
    for d in ('Mods', 'Mods/_example'):
        os.makedirs(os.path.join(dst, d), exist_ok=True)
    for name in ('README_RU.txt',):
        p = os.path.join(src, 'Mods', name)
        if os.path.isfile(p): shutil.copy2(p, os.path.join(dst, 'Mods', name))
    ex = os.path.join(src, 'Mods', '_example')
    if os.path.isdir(ex):
        shutil.copytree(ex, os.path.join(dst, 'Mods', '_example'), dirs_exist_ok=True)
    # Same pass the payload gets: internal QA notes out, captured user paths rewritten.
    import subprocess
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                 'sanitize_release_root.py'), dst], check=True)
    # Same pass the payload gets: internal QA notes out, captured user paths rewritten.
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                 'sanitize_release_root.py'), dst], check=True)
    report = {'source': src, 'staging': dst, 'files_copied': copied, 'skipped': sorted(skipped)}
    with open(dst.rstrip('\/') + '.STAGING_REPORT.json', 'w', encoding='utf-8') as h:  # beside, not inside: everything inside ships
        json.dump(report, h, ensure_ascii=False, indent=2)
    print(json.dumps({k: v for k, v in report.items() if k != 'skipped'}, ensure_ascii=False))
    print('skipped top-level:', sorted({s.split('/')[0] for s in skipped}))


if __name__ == '__main__':
    main()
