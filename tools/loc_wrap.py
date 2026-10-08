"""Wraps C# string literals on chosen lines in T("...") / F("...", args) for translation (see Loc.cs).

Usage: python tools/loc_wrap.py < spec    where each spec line is
    path/to/File.cs:LINE            wrap every literal on that line that has a lowercase letter in it
    path/to/File.cs:LINE:2,3        wrap only the 2nd and 3rd literals on the line (1-based)
Plain "text" becomes T("text"); $"text {expr:fmt}" becomes F("text {0:fmt}", expr). Verbatim (@"...") literals and
ones already inside T( or F( are left alone. Run from the repo root (paths are relative to src/DaisysApp).
"""
import re
import sys
from collections import defaultdict

ROOT = 'src/DaisysApp/'


def scan(line):
    """Yields (start, end, kind, text) for each string literal; kind is '"' or '$'. Stops at a // comment."""
    i, n = 0, len(line)
    while i < n:
        c = line[i]
        if c == '/' and line.startswith('//', i):
            return
        if c == "'":  # char literal
            j = line.index("'", i + 1 + (1 if line[i + 1] == '\\' else 0) + 1) if i + 1 < n else n
            i = j + 1
            continue
        if c == '@' and line.startswith('@"', i) or line.startswith('$@"', i) or line.startswith('@$"', i):
            j = line.index('"', i) + 1
            while j < n:
                if line[j] == '"' and line.startswith('""', j):
                    j += 2
                    continue
                if line[j] == '"':
                    break
                j += 1
            i = j + 1
            continue
        if c == '$' and line.startswith('$"', i):
            j = end_interp(line, i + 2)
            yield i, j, '$', line[i + 2:j - 1]
            i = j
            continue
        if c == '"':
            j = i + 1
            while line[j] != '"':
                j += 2 if line[j] == '\\' else 1
            yield i, j + 1, '"', line[i + 1:j]
            i = j + 1
            continue
        i += 1


def end_interp(line, j):
    """Index just past the closing quote of an interpolated string whose body starts at j."""
    while True:
        c = line[j]
        if c == '\\':
            j += 2
        elif c == '{' and line.startswith('{{', j):
            j += 2
        elif c == '}' and line.startswith('}}', j):
            j += 2
        elif c == '{':
            j = end_hole(line, j + 1)
        elif c == '"':
            return j + 1
        else:
            j += 1


def end_hole(line, j):
    depth = 0
    while True:
        c = line[j]
        if c == '"' or line.startswith('$"', j):
            if c == '$':
                j = end_interp(line, j + 2)
            else:
                k = j + 1
                while line[k] != '"':
                    k += 2 if line[k] == '\\' else 1
                j = k + 1
            continue
        if c == "'":
            j = line.index("'", j + 2 if line[j + 1] != '\\' else j + 3) + 1
            continue
        if c in '([{':
            depth += 1
        elif c in ')]':
            depth -= 1
        elif c == '}':
            if depth == 0:
                return j + 1
            depth -= 1
        j += 1


def holes(body):
    """Splits an interpolated body into a format string and the hole expressions."""
    out, args, j = [], [], 0
    while j < len(body):
        c = body[j]
        if body.startswith('{{', j) or body.startswith('}}', j):
            out.append(body[j:j + 2])
            j += 2
            continue
        if c == '\\':
            out.append(body[j:j + 2])
            j += 2
            continue
        if c == '{':
            k = end_hole(body + '"', j + 1)  # index past '}'
            inner = body[j + 1:k - 1]
            expr, fmt = split_format(inner)
            out.append('{%d%s}' % (len(args), fmt))
            args.append(expr.strip())
            j = k
            continue
        out.append(c)
        j += 1
    return ''.join(out), args


def split_format(inner):
    """'x:0.0' -> ('x', ':0.0'); ',5' alignment too; colons inside (), [], strings or ?: don't count."""
    depth, j, q = 0, 0, 0
    while j < len(inner):
        c = inner[j]
        if c == '"':
            k = j + 1
            while inner[k] != '"':
                k += 2 if inner[k] == '\\' else 1
            j = k + 1
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        elif c == '?' and depth == 0 and not inner.startswith('??', j) and not inner.startswith('?.', j) and (j == 0 or inner[j - 1] != '?'):
            q += 1
        elif c == ':' and depth == 0:
            if q > 0:
                q -= 1
            else:
                return inner[:j], inner[j:]
        elif c == ',' and depth == 0:
            # alignment ({x,5}) – keep it with the format
            m = re.match(r',\s*-?\d+', inner[j:])
            if m:
                return inner[:j], inner[j:]
        j += 1
    return inner, ''


def wrap(line, which):
    pieces, last, k = [], 0, 0
    for s, e, kind, text in list(scan(line)):
        k += 1
        if which and k not in which:
            continue
        if not which and not re.search('[a-z]', text):
            continue
        before = line[:s].rstrip()
        if before.endswith('T(') or before.endswith('F(') or before.endswith('Any('):
            continue
        if kind == '"':
            new = 'T("%s")' % text
        else:
            fmt, args = holes(text)
            if not args:
                new = 'T("%s")' % fmt
            elif len(args) > 5:
                raise SystemExit(f'too many holes: {line.strip()}')
            else:
                new = 'F("%s", %s)' % (fmt, ', '.join(args))
        pieces.append(line[last:s])
        pieces.append(new)
        last = e
    pieces.append(line[last:])
    return ''.join(pieces)


def main():
    todo = defaultdict(dict)
    for raw in sys.stdin:
        raw = raw.strip()
        if not raw or raw.startswith('#'):
            continue
        parts = raw.split(':')
        path, ln = parts[0], int(parts[1])
        which = {int(x) for x in parts[2].split(',')} if len(parts) > 2 and parts[2] else set()
        todo[path][ln] = which
    for path, lines in todo.items():
        full = ROOT + path.replace('\\', '/')
        with open(full, encoding='utf-8', newline='') as f:
            content = f.read().split('\n')
        for ln, which in lines.items():
            old = content[ln - 1]
            new = wrap(old, which)
            if new == old:
                print(f'unchanged {path}:{ln}: {old.strip()}')
            content[ln - 1] = new
        with open(full, 'w', encoding='utf-8', newline='') as f:
            f.write('\n'.join(content))
        print(f'{path}: {len(lines)} lines')


def inside_call(line, start, spans):
    """True if the literal at start is an argument of T(, F(, P( or Any( (strings skipped when matching brackets)."""
    stack = []
    i = 0
    while i < start:
        span = next((sp for sp in spans if sp[0] == i), None)
        if span:
            i = span[1]
            continue
        c = line[i]
        if c == '(':
            m = re.search(r'(\w+)\s*$', line[:i])
            stack.append(m.group(1) if m else '')
        elif c == ')' and stack:
            stack.pop()
        i += 1
    return any(name in ('T', 'F', 'P', 'Any') for name in stack)


def list_files(paths, leftovers=False):
    """Prints each line with a literal that has letters in it, numbering the literals (for picking with :n)."""
    for path in paths:
        rel = path.replace('\\', '/').removeprefix(ROOT)
        with open(ROOT + rel, encoding='utf-8') as f:
            for ln, line in enumerate(f, 1):
                st = line.strip()
                if st.startswith('//') or st.startswith('[') or 'ErrorLog.' in line or 'nameof(' in line:
                    continue
                try:
                    lits = [(k, kind, t) for k, (s, e, kind, t) in enumerate(scan(line), 1)]
                except (IndexError, ValueError):
                    print(f'{rel}:{ln}: (unreadable: {st[:100]})')
                    continue
                lits = [l for l in lits if re.search('[a-z]{2}', l[2])]
                if leftovers:
                    spans = [(a, b) for a, b, _, _ in scan(line)]
                    keep = []
                    for k, kind, t in lits:
                        a = spans[k - 1][0]
                        if not inside_call(line, a, spans) and ' ' in t.strip() and re.search(r'[a-z]{2,} [a-z]', t):
                            keep.append((k, kind, t))
                    lits = keep
                if lits:
                    print(f'{rel}:{ln}: ' + ' | '.join(f'[{k}]{"$" if kind == "$" else ""}"{t[:90]}"' for k, kind, t in lits))


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] in ('--list', '--left'):
        list_files(sys.argv[2:], leftovers=sys.argv[1] == '--left')
    else:
        main()
