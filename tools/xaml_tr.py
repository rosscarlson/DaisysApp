"""Turns literal text in XAML attributes into {l:Tr '...'} (see Localization/TrExtension.cs) and adds xmlns:l.

    python tools/xaml_tr.py file.xaml [more.xaml...]

Only Text, Content, ToolTip, Header and Title attributes with letters in them, that aren't already markup ({...}).
"""
import re
import sys

ATTR = re.compile(r'(?<=\s)(Text|Content|ToolTip|Header|Title)="([^"{][^"]*)"')
BS = chr(92)  # a backslash


def escape(raw):
    v = raw.replace('&apos;', "'")
    for c in (BS, "'", '{', '}'):
        v = v.replace(c, BS + c)
    return v


def convert(s):
    def rep(m):
        if not re.search('[A-Za-z]{2}', re.sub('&#x?[0-9A-Fa-f]+;', '', m.group(2))):
            return m.group(0)
        return m.group(1) + '="{l:Tr \'' + escape(m.group(2)) + '\'}"'
    s, n = ATTR.subn(rep, s)
    if n and 'xmlns:l=' not in s:
        s = s.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
                      'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n             xmlns:l="clr-namespace:DaisysApp"', 1)
    return s, n


for path in sys.argv[1:]:
    raw = open(path, encoding='utf-8-sig', newline='').read()
    new, n = convert(raw)
    if n:
        open(path, 'w', encoding='utf-8', newline='').write(new)
    print(f'{path}: {n}')
