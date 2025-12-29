import os
import re
root = r"P:\\PacBros"
script_guids = set()
for dirpath, _, files in os.walk(root):
    for name in files:
        if not name.endswith('.cs.meta'):
            continue
        path = os.path.join(dirpath, name)
        try:
            with open(path, encoding='utf-8', errors='ignore') as fh:
                for line in fh:
                    if line.startswith('guid: '):
                        script_guids.add(line.split(':', 1)[1].strip())
                        break
        except OSError:
            pass
pattern = re.compile(r"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32})", re.IGNORECASE)
missing = set()
target_exts = ('.prefab', '.unity', '.asset')
assets_root = os.path.join(root, 'Assets')
for dirpath, _, files in os.walk(assets_root):
    for name in files:
        if not name.lower().endswith(target_exts):
            continue
        path = os.path.join(dirpath, name)
        try:
            with open(path, encoding='utf-8', errors='ignore') as fh:
                text = fh.read()
        except OSError:
            continue
        for match in pattern.finditer(text):
            guid = match.group(1).lower()
            if guid not in script_guids:
                missing.add((os.path.relpath(path, root), guid))
                break
if missing:
    for rel, guid in sorted(missing):
        print(f"{rel} -> {guid}")
else:
    print('No missing scripts under Assets')
