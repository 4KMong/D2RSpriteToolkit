from pathlib import Path

p = Path('bundle/post_patch.py')
s = p.read_text(encoding='utf-8')

old = '''def replace_c_function(rel, name, replacement, desc):
    s = read(rel)
    marker = name + "("
    pos = s.find(marker)
    if pos < 0: raise PatchError(f"{desc}: function {name} not found in {rel}")
'''
new = '''def replace_c_function(rel, name, replacement, desc):
    s = read(rel)
    marker = name + "("

    def find_exact_name(start=0):
        # Avoid matching suffixes such as window_building_draw_market_orders().
        pos = s.find(marker, start)
        while pos >= 0:
            if pos == 0 or not (s[pos - 1].isalnum() or s[pos - 1] == '_'):
                return pos
            pos = s.find(marker, pos + len(marker))
        return -1

    pos = find_exact_name()
    if pos < 0: raise PatchError(f"{desc}: function {name} not found in {rel}")
'''
if old not in s:
    raise SystemExit('replace_c_function anchor not found')
s = s.replace(old, new, 1)

old = '''        pos2 = s.find(marker, pos + len(marker))
        if pos2 < 0: raise PatchError(f"{desc}: definition for {name} not found")
'''
new = '''        pos2 = find_exact_name(pos + len(marker))
        if pos2 < 0: raise PatchError(f"{desc}: definition for {name} not found")
'''
if old not in s:
    raise SystemExit('definition search anchor not found')
s = s.replace(old, new, 1)

anchor = "# Replace '=' placeholders with an explicit Maintain label.\n"
extra = '''# distribution.c uses translation_for() for the localized Maintain label.
insert_after_once('src/window/building/distribution.c',
''' + "'''#include \"scenario/property.h\"\\n'''" + ''',
''' + "'''#include \"translation/translation.h\"\\n'''" + ''',
'include translation API in distribution window')

'''
if anchor not in s:
    raise SystemExit('maintain-label anchor not found')
s = s.replace(anchor, extra + anchor, 1)

p.write_text(s, encoding='utf-8', newline='\n')
print('post_patch.py corrected for exact function matching + translation include')
