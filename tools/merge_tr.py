"""Merges translation tables into the lang files: python tools/merge_tr.py Module table.json [more pairs...]

A table is a JSON list of [english, es, fr, pt] rows. Rows whose English isn't one of the module's texts (en.json) are
reported and skipped, so a typo can't add a dead entry. Existing translations are replaced by the table's.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from strings import lang_dir, read, write  # noqa: E402

LANGS = [('es', 'Español'), ('fr', 'Français'), ('pt', 'Português')]

args = sys.argv[1:]
for module, table in zip(args[::2], args[1::2]):
    d = lang_dir(module)
    en = read(os.path.join(d, 'en.json'))
    rows = json.load(open(table, encoding='utf-8'))
    files = {code: (read(os.path.join(d, code + '.json')) or {'_language': name}) for code, name in LANGS}
    bad = 0
    for row in rows:
        if row[0] not in en:
            print(f'{module}: not a text: {row[0]!r}')
            bad += 1
            continue
        for (code, _), text in zip(LANGS, row[1:]):
            files[code][row[0]] = text
    for code, data in files.items():
        # in en.json's order, then anything else
        ordered = {k: data[k] for k in ['_language'] + [k for k in en if not k.startswith('_')] if k in data}
        write(os.path.join(d, code + '.json'), ordered)
    missing = [k for k in en if not k.startswith('_') and k not in files['es']]
    print(f'{module}: {len(rows) - bad} merged, {bad} skipped, {len(missing)} still missing')
    for k in missing:
        print(f'   missing: {k!r}')
