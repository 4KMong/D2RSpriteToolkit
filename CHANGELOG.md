# Changelog

## 4.0.1 - Frame Template Dimension Fix

- Changed PNG-to-Sprite frame-template conversion to trust the template frame count instead of its incorrect total dimensions
- Recalculate frame width, total width, height, bytes per pixel, and payload size from the source PNG
- Reject conversion only when the PNG width cannot be divided evenly by the template frame count
- Preserve the remaining compatible template header values

## 4.0.0 - Initial Public Release

- Added low-resolution PNG generation with custom size and suffix options
- Added PNG and Sprite file or directory drag-and-drop support
- Added Sprite header analysis, preview, and metadata display
- Added Sprite-to-PNG and PNG-to-Sprite conversion
- Added frame-template support for preserving Sprite header and frame structure
- Added batch file renaming with prefix, suffix, replacement, and partial deletion tools
- Added embedded Korean and English language resources
- Added system-tray support with a dedicated tray icon
- Unified the application name, file version, and manifest version as 4.0.0
- Added an unofficial fan-project notice and a source-available license
