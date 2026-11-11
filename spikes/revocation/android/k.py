#!/usr/bin/env python3
"""Drives the emulator through adb: uiautomator dumps, taps by text, typed text, keys, screenshots.

Usage:  python3 k.py <command> [args]   or   python3 k.py run <flow file> [VAR=value ...]
Commands (one per line in a flow file; '#' starts a comment; ${VAR} is replaced from the arguments):
  list                      every node with text, description or id, and its center
  tap REGEX[#n]             tap the n-th (default 1st) node whose text, content-desc or resource-id matches
  tapxy X Y                 tap a point
  waitfor REGEX [secs]      dump until a node matches (default 30 s), else fail
  maybe REGEX [secs]        tap it if it appears within secs (default 3), else carry on
  gone REGEX [secs]         wait until no node matches
  assert REGEX              fail unless a node matches now
  text STRING               type (spaces as %s)
  key CODE                  input keyevent (BACK, ENTER, TAB, ESCAPE, or a number)
  sleep SECS
  shot NAME                 screenshot to out/shots/NAME.png
  sh ARGS...                adb shell ARGS
  adb ARGS...               adb ARGS
  log TEXT                  write TEXT to the log
Matching is case-insensitive re.search. Every step is logged with its UTC time.
"""
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

# The scripts beside this file source env.sh, which sets both; run alone, this needs them set.
ADB = os.path.join(os.environ['ANDROID_HOME'], 'platform-tools', 'adb')
D = os.environ['KPG_ANDROID_WORK']


def now():
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'


def log(msg):
    print(f'[{now()}] {msg}', flush=True)


def adb(*a):
    r = subprocess.run([ADB, *a], capture_output=True, text=True, errors='replace')
    return (r.stdout or '') + (r.stderr or '')


def dump():
    last = ''
    for _ in range(6):
        out = adb('exec-out', 'uiautomator', 'dump', '/dev/tty')
        i, j = out.find('<?xml'), out.rfind('</hierarchy>')
        if i >= 0 and j > 0:
            try:
                return ET.fromstring(out[i:j + len('</hierarchy>')])
            except ET.ParseError:
                pass
        last = out
        time.sleep(1)
    raise RuntimeError('dump failed: ' + last[-300:])


def center(b):
    m = re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]', b or '')
    if not m:
        return None
    x1, y1, x2, y2 = map(int, m.groups())
    return (x1 + x2) // 2, (y1 + y2) // 2


def nodes(root):
    for n in root.iter('node'):
        yield n.attrib


def find(root, pat):
    nth = 1
    if '#' in pat and pat.rsplit('#', 1)[1].isdigit():
        pat, k = pat.rsplit('#', 1)
        nth = int(k)
    want_id = None
    if '@' in pat:  # ID@REGEX: the resource id ends with /ID and the text or description matches
        want_id, pat = pat.split('@', 1)
    rx = re.compile(pat, re.I)
    hits = []
    for a in nodes(root):
        if want_id is not None:
            if a.get('resource-id', '').endswith('/' + want_id) and (rx.search(a.get('text', '')) or rx.search(a.get('content-desc', ''))):
                hits.append(a)
            continue
        for f in ('text', 'content-desc', 'resource-id'):
            v = a.get(f, '')
            if v and rx.search(v):
                hits.append(a)
                break
    return hits[nth - 1] if len(hits) >= nth else None


def show(root):
    for a in nodes(root):
        t, d, r = a.get('text', ''), a.get('content-desc', ''), a.get('resource-id', '')
        if not (t or d or r):
            continue
        flags = ''.join(c for c, k in (('c', 'clickable'), ('k', 'checked'), ('f', 'focused'), ('x', 'checkable')) if a.get(k) == 'true')
        if a.get('enabled') == 'false':
            flags += 'D'
        cls = a.get('class', '').rsplit('.', 1)[-1]
        print(f'  {cls:<14} {flags:<4} {str(center(a.get("bounds"))):<12} id={r.rsplit("/", 1)[-1]!s:<28} text={t!r} desc={d!r}')


def tap_node(a):
    x, y = center(a['bounds'])
    adb('shell', 'input', 'tap', str(x), str(y))
    return x, y


def wait(pat, secs):
    end = time.time() + secs
    while True:
        root = dump()
        a = find(root, pat)
        if a is not None:
            return root, a
        if time.time() > end:
            return root, None
        time.sleep(0.7)


def step(cmd, args):
    if cmd == 'list':
        show(dump())
    elif cmd == 'tap':
        root, a = wait(args[0], 15)
        if a is None:
            show(root)
            raise RuntimeError('tap: no node matches ' + args[0])
        log(f'  tapped {tap_node(a)} {a.get("text") or a.get("content-desc") or a.get("resource-id")!r}')
    elif cmd == 'focusto':
        # DocumentsUI ignores `input tap` on its items (the injected events carry no finger tool
        # type), so its lists are walked with the keyboard: DPAD_DOWN until the focused item holds
        # REGEX, then ENTER.
        rx = re.compile(args[0], re.I)
        for _ in range(40):
            root = dump()
            hit = None
            for n in root.iter('node'):
                if n.get('focused') == 'true' and any(rx.search(c.get('text', '')) for c in n.iter('node')):
                    hit = n
                    break
            if hit is not None:
                adb('shell', 'input', 'keyevent', 'KEYCODE_ENTER')
                log(f'  focused and entered {args[0]!r}')
                return
            adb('shell', 'input', 'keyevent', 'KEYCODE_DPAD_DOWN')
            time.sleep(0.3)
        show(root)
        raise RuntimeError('focusto: never focused ' + args[0])
    elif cmd == 'tapor':
        # tapor REGEX X Y: tap the node where the dump puts it, or at X Y when the dump gives it no
        # size.
        root, a = wait(args[0], 15)
        if a is None:
            show(root)
            raise RuntimeError('tapor: no node matches ' + args[0])
        if center(a.get('bounds')) == (0, 0):
            adb('shell', 'input', 'tap', args[1], args[2])
            log(f'  tapped ({args[1]}, {args[2]}) for {args[0]!r} (no bounds in the dump)')
        else:
            log(f'  tapped {tap_node(a)} {args[0]!r}')
    elif cmd == 'tapxy':
        adb('shell', 'input', 'tap', args[0], args[1])
    elif cmd == 'waitfor':
        root, a = wait(args[0], float(args[1]) if len(args) > 1 else 30)
        if a is None:
            show(root)
            raise RuntimeError('waitfor: timed out on ' + args[0])
    elif cmd == 'maybe':
        root, a = wait(args[0], float(args[1]) if len(args) > 1 else 3)
        if a is not None:
            log(f'  maybe tapped {tap_node(a)} {a.get("text") or a.get("content-desc")!r}')
        else:
            log('  maybe: absent')
    elif cmd == 'drawer':
        # drawer TARGET: open a navigation drawer until TARGET shows, alternating an edge swipe and
        # the toolbar's drawer button (either was seen to be ignored now and then).
        for attempt in range(8):
            if attempt % 2 == 0:
                adb('shell', 'input', 'swipe', '1', '1100', '750', '1100', '400')
            else:
                root, a = wait('Navigation.drawer.open', 3)
                if a is not None:
                    tap_node(a)
            root, a = wait(args[0], 4)
            if a is not None:
                log(f'  drawer open after {attempt + 1} attempt(s)')
                return
        show(root)
        raise RuntimeError('drawer: never showed ' + args[0])
    elif cmd == 'pick':
        # pick FILE: in the system file picker, pick /sdcard/Download/FILE; when the picker already
        # lists FILE (it remembers the last folder), straight away, else by the device root and
        # Download.
        name = args[0]
        root, a = wait('Show.roots', 20)
        if a is None:
            show(root)
            raise RuntimeError('pick: no file picker')
        crumbs = [n.get('text') for n in nodes(root) if n.get('resource-id', '').endswith('/breadcrumb_text')]
        listed = find(root, 'title@^' + re.escape(name) + '$') is not None
        if not (listed and crumbs and crumbs[-1] == 'Download'):
            step('tap', ['Show.roots'])
            step('tap', ['title@^sdk_gphone'])
            time.sleep(2)
            step('focusto', ['^Download$'])
            time.sleep(2)
        step('focusto', ['^' + re.escape(name) + '$'])
    elif cmd in ('type', 'typepw'):
        # type REGEX TEXT: tap the field, wait until it holds the focus, type, and (type only)
        # check that the field now holds TEXT; up to four attempts. Typing before the focus lands
        # loses the text.
        txt = ' '.join(args[1:])
        for attempt in range(4):
            root, a = wait(args[0], 15)
            if a is None:
                show(root)
                raise RuntimeError('type: no field matches ' + args[0])
            b = a['bounds']
            tap_node(a)
            focused = False
            for _ in range(4):
                time.sleep(0.4)
                for n in nodes(dump()):
                    if n.get('focused') == 'true' and 'EditText' in n.get('class', '') and n.get('bounds') == b:
                        focused = True
                if focused:
                    break
            if not focused:
                log(f'  type: field never took the focus (attempt {attempt + 1})')
                continue
            adb('shell', 'input', 'text', txt.replace(' ', '%s'))
            if cmd == 'typepw':
                log('  typed into the focused field')
                return
            time.sleep(0.6)
            for n in nodes(dump()):
                if n.get('focused') == 'true' and 'EditText' in n.get('class', '') and txt in n.get('text', ''):
                    log(f'  typed {txt!r}')
                    return
            log(f'  type: text did not land (attempt {attempt + 1})')
        raise RuntimeError('type: gave up on ' + args[0])
    elif cmd == 'tapuntil':
        # tapuntil REGEX TARGET [secs]: tap REGEX, and again every few seconds, until TARGET is on
        # screen.
        end = time.time() + (float(args[2]) if len(args) > 2 else 30)
        while True:
            root, a = wait(args[0], 10)
            if find(root, args[1]) is not None:
                return
            if a is not None:
                log(f'  tapped {tap_node(a)} {a.get("text") or a.get("content-desc") or a.get("resource-id")!r}')
            root, b = wait(args[1], 5)
            if b is not None:
                return
            if time.time() > end:
                show(root)
                raise RuntimeError('tapuntil: never reached ' + args[1])
    elif cmd == 'clear':
        # clear BUTTON TARGET [secs]: tap BUTTON (logging the dialog's title) until TARGET is on
        # screen.
        end = time.time() + (float(args[2]) if len(args) > 2 else 30)
        while True:
            root = dump()
            if find(root, args[1]) is not None:
                return
            a = find(root, args[0])
            if a is not None:
                t = find(root, 'alertTitle')
                log(f'  cleared {(t.get("text") if t is not None else "")!r} with {tap_node(a)}')
                time.sleep(1)
            elif time.time() > end:
                show(root)
                raise RuntimeError('clear: never reached ' + args[1])
            else:
                time.sleep(0.7)
    elif cmd == 'gone':
        end = time.time() + (float(args[1]) if len(args) > 1 else 30)
        while find(dump(), args[0]) is not None:
            if time.time() > end:
                raise RuntimeError('gone: still there ' + args[0])
            time.sleep(0.7)
    elif cmd == 'assert':
        root = dump()
        if find(root, args[0]) is None:
            show(root)
            raise RuntimeError('assert failed: ' + args[0])
    elif cmd == 'text':
        adb('shell', 'input', 'text', ' '.join(args).replace(' ', '%s'))
    elif cmd == 'key':
        k = args[0] if args[0].isdigit() else 'KEYCODE_' + args[0].upper()
        adb('shell', 'input', 'keyevent', k)
    elif cmd == 'sleep':
        time.sleep(float(args[0]))
    elif cmd == 'shot':
        os.makedirs(f'{D}/out/shots', exist_ok=True)
        with open(f'{D}/out/shots/{args[0]}.png', 'wb') as f:
            f.write(subprocess.run([ADB, 'exec-out', 'screencap', '-p'], capture_output=True).stdout)
    elif cmd == 'sh':
        print(adb('shell', *args), end='')
    elif cmd == 'adb':
        print(adb(*args), end='')
    elif cmd == 'log':
        log('  ' + ' '.join(args))
    else:
        raise RuntimeError('unknown command ' + cmd)


def run_flow(path, env):
    with open(path, encoding='utf-8') as f:
        lines = f.read().splitlines()
    for n, line in enumerate(lines, 1):
        line = line.split(' #', 1)[0].strip() if not line.strip().startswith('#') else ''
        if not line:
            continue
        for k, v in env.items():
            line = line.replace('${' + k + '}', v)
        parts = line.split()
        log(f'{os.path.basename(path)}:{n} {line}')
        if parts[0] == 'include':
            run_flow(os.path.join(os.path.dirname(path), parts[1]), env)
            continue
        step(parts[0], parts[1:])


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    try:
        if sys.argv[1] == 'run':
            env = dict(a.split('=', 1) for a in sys.argv[3:])
            run_flow(sys.argv[2], env)
        else:
            step(sys.argv[1], sys.argv[2:])
    except RuntimeError as e:
        log('FAILED: ' + str(e))
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
