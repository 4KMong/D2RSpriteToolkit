from pathlib import Path
import subprocess, sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else '.')

def must_replace(path, old, new, label):
    p = root / path
    s = p.read_text(encoding='utf-8')
    if old not in s:
        raise SystemExit(f'ERROR: {label}: expected text not found in {path}')
    p.write_text(s.replace(old, new, 1), encoding='utf-8', newline='\n')
    print(f'OK  {label}')

# Clean QoL v2 used Julius's stock menu renderer. Grid menu strings are normal c3.eng
# entries (group 19, items 64/65), so translation-key support in graphics/menu.c is unnecessary.
subprocess.run(['git', 'checkout', '--', 'src/graphics/menu.c'], cwd=root, check=True)
print('OK  restore stock graphics/menu.c')

p = root / 'src/widget/top_menu.c'
s = p.read_text(encoding='utf-8')
s2 = s.replace('#include "translation/translation.h"\n', '', 1)
if s2 == s:
    raise SystemExit('ERROR: remove top-menu translation include: expected include not found')
p.write_text(s2, encoding='utf-8', newline='\n')
print('OK  remove top-menu translation include')

must_replace('src/widget/top_menu.c',
'''    {-1, TR_TOP_MENU_GRID_ENABLE, menu_options_grid, 0},''',
'''    {19, 64, menu_options_grid, 0},''',
'bind grid menu to c3.eng group 19 item 64')

must_replace('src/widget/top_menu.c',
'''static void set_text_for_grid(void)\n{\n    menu_update_text(&menu[INDEX_OPTIONS], 5,\n        config_get(CONFIG_UI_SHOW_GRID) ? TR_TOP_MENU_GRID_DISABLE : TR_TOP_MENU_GRID_ENABLE);\n}\n''',
'''static void set_text_for_grid(void)\n{\n    menu_update_text(&menu[INDEX_OPTIONS], 5,\n        config_get(CONFIG_UI_SHOW_GRID) ? 65 : 64);\n}\n''',
'use c3.eng grid on/off labels')

# Hard guard against the previously rejected market-construction-range experiment.
for path in root.rglob('*'):
    if not path.is_file() or '.git' in path.parts or 'build' in path.parts:
        continue
    try:
        text = path.read_text(encoding='utf-8')
    except (UnicodeDecodeError, OSError):
        continue
    for banned in ('CONFIG_UI_SHOW_MARKET_RANGE', 'ui_show_market_range'):
        if banned in text:
            raise SystemExit(f'ERROR: banned market-range marker {banned} found in {path.relative_to(root)}')
print('OK  banned market construction range markers absent')
