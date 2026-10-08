"""Collects every translatable text and writes each module's lang/en.json (see src/DaisysApp/Localization/Loc.cs).

    python tools/strings.py                 # rewrite the en.json files and report what other languages are missing
    python tools/strings.py --missing es    # print the texts es.json files are missing, as JSON per module

Texts come from the C# code (T("..."), F("...", ...), P(n, "...", "...") and the [Applet] title and description) and
from the XAML (Text, Content, ToolTip, Header and Title attributes, and Run/TextBlock text that isn't bound). The main
window's go to src/DaisysApp/lang/, each applet's to its folder's lang/. Other languages' files keep their order and
values; texts no longer used are reported (and removed with --prune).
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(__file__))
from loc_wrap import scan  # noqa: E402

SRC = os.path.join(os.path.dirname(__file__), '..', 'src', 'DaisysApp')
SHELL = 'Shell'
ATTRS = ('Text', 'Content', 'ToolTip', 'Header', 'Title')
UNESCAPE = re.compile(r'\\(.)')  # a backslash and the character it escapes
INLINE = ('TextBlock', 'Run', 'Bold', 'Italic', 'Underline', 'Hyperlink', 'Span')
NOTE = ('Copy this file to your language\'s code (e.g. de.json) and translate each value (the text on the right). '
        'Keep the English on the left as it is, and keep {0}, {1}... (the app fills them in). Anything left out stays in English.')


def module_of(path):
    parts = os.path.normpath(path).split(os.sep)
    if 'Applets' in parts:
        i = parts.index('Applets')
        if i + 2 < len(parts):
            return parts[i + 1]
    return SHELL


def lang_dir(module):
    return os.path.join(SRC, 'lang') if module == SHELL else os.path.join(SRC, 'Applets', module, 'lang')


def unescape(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            if n == 'u':
                out.append(chr(int(s[i + 2:i + 6], 16)))
                i += 6
                continue
            out.append({'n': '\n', 'r': '\r', 't': '\t', '0': '\0'}.get(n, n))
            i += 2
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def calls(line, spans):
    """For each literal: (name of the innermost call it's an argument of, index of the argument)."""
    result = {}
    stack = []  # [name, argument index]
    i = 0
    while i < len(line):
        sp = next((s for s in spans if s[0] == i), None)
        if sp:
            result[i] = (stack[-1][0], stack[-1][1]) if stack else (None, -1)
            i = sp[1]
            continue
        c = line[i]
        if c == '/' and line.startswith('//', i):
            break
        if c in '([{':
            m = re.search(r'(\w+)\s*$', line[:i]) if c == '(' else None
            stack.append([m.group(1) if m else '', 0])
        elif c in ')]}' and stack:
            stack.pop()
        elif c == ',' and stack:
            stack[-1][1] += 1
        i += 1
    return result


def from_cs(path, add):
    text = open(path, encoding='utf-8').read()
    for m in re.finditer(r'\[Applet\((.*?)\)\]', text, re.S):
        lits = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))
        if len(lits) >= 2:
            add(unescape(lits[1]))  # the title
        d = re.search(r'Description\s*=\s*"((?:[^"\\]|\\.)*)"', m.group(1))
        if d:
            add(unescape(d.group(1)))
    for line in text.split('\n'):
        st = line.strip()
        if st.startswith('//'):
            continue
        try:
            lits = list(scan(line))
        except (IndexError, ValueError):
            continue
        if not lits:
            continue
        where = calls(line, [(a, b) for a, b, _, _ in lits])
        for a, b, kind, t in lits:
            name, arg = where.get(a, (None, -1))
            if kind == '"' and ((name in ('T', 'F') and arg == 0) or (name == 'P' and arg in (1, 2))):
                add(unescape(t))


def from_xaml(path, add):
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        print(f'  can\'t read {path}: {e}')
        return
    for el in root.iter():
        for k, v in el.attrib.items():
            m = re.fullmatch(r"\{l:Tr '(.*)'\}", v)
            if m:
                add(UNESCAPE.sub(lambda e: e.group(1), m.group(1)).strip())  # {l:Tr 'text'}, with backslash escapes
            elif k in ATTRS and wanted(v):
                add(v)
        tag = el.tag.split('}')[-1]
        if tag in INLINE:
            # text between inline elements (<Run>, <LineBreak/>...) is a run of its own; XAML collapses its whitespace
            for piece in [el.text] + [c.tail for c in el if c.tag.split('}')[-1] in INLINE + ('LineBreak',)]:
                if piece and wanted(' '.join(piece.split())):
                    add(' '.join(piece.split()))


def wanted(v):
    return bool(v) and not v.startswith('{') and re.search('[A-Za-z]{2}', v)


def collect():
    found = {}
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj', 'lang')]
        for f in sorted(files):
            p = os.path.join(root, f)
            mod = module_of(os.path.relpath(p, SRC))
            keys = found.setdefault(mod, {})
            add = lambda s, keys=keys: keys.setdefault(s, None)
            if f.endswith('.cs') and f != 'Loc.cs':
                from_cs(p, add)
            elif f.endswith('.xaml'):
                from_xaml(p, add)
    return {m: list(k) for m, k in found.items() if k}


def read(path):
    if not os.path.exists(path):
        return None
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')


def main():
    args = sys.argv[1:]
    modules = collect()
    if args[:1] == ['--missing']:
        code = args[1]
        out = {}
        for m, keys in modules.items():
            have = read(os.path.join(lang_dir(m), code + '.json')) or {}
            miss = [k for k in keys if k not in have]
            if miss:
                out[m] = miss
        json.dump(out, sys.stdout, ensure_ascii=False, indent=1)
        return
    total = 0
    for m, keys in sorted(modules.items()):
        d = lang_dir(m)
        en = {'_language': 'English', '_note': NOTE}
        en.update({k: k for k in keys})
        write(os.path.join(d, 'en.json'), en)
        total += len(keys)
        line = f'{m}: {len(keys)} texts'
        for f in sorted(os.listdir(d)):
            if not f.endswith('.json') or f == 'en.json':
                continue
            data = read(os.path.join(d, f))
            miss = [k for k in keys if k not in data]
            stale = [k for k in data if not k.startswith('_') and k not in en]
            bad = [k for k, v in data.items() if not k.startswith('_') and k in en and sorted(re.findall(r'\{\d+[^}]*\}', k)) != sorted(re.findall(r'\{\d+[^}]*\}', v))]
            line += f' | {f}: {len(miss)} missing, {len(stale)} unused, {len(bad)} with different {{n}}'
            if '--prune' in args and stale:
                for k in stale:
                    del data[k]
                write(os.path.join(d, f), data)
            for k in bad:
                print(f'    {f} placeholders differ: {k!r} -> {data[k]!r}')
        print(line)
    print(f'{total} texts in all')


if __name__ == '__main__':
    main()
