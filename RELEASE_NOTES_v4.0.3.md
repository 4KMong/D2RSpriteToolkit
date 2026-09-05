# D2R Sprite Toolkit v4.0.3

## File List Visibility Update

Version 4.0.3 makes the three generated-file controls behave as direct, independent visibility filters for the file list.

## Changes

- `.lowend.png`, `.sprite`, and `.lowend.sprite` visibility can now be toggled independently.
- Turning a filter off hides matching entries without removing them from the loaded file set.
- Turning the filter back on immediately restores the hidden entries.
- `.lowend.sprite` no longer depends on the `.sprite` visibility option.
- When conversion creates a file whose visibility option is currently off, the matching option is automatically enabled so the new file is shown immediately.
- Newly generated files are added directly to the file list, including outputs created inside `output_png` and `output_sprite`.
- After reviewing a generated file, the corresponding visibility option can still be turned off normally.

## Installation

1. Download `D2RSpriteToolkit_v4.0.3_Windows.zip`.
2. Extract the archive.
3. Run `D2RSpriteTK.exe`.

No installer is required.
