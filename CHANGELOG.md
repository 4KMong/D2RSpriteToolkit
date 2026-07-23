# Changelog

## 4.0.2 - Safe Frame-Template Rebuild

- Removed the incorrect requirement that a frame-template Sprite must report BPP 4
- Read same-name Sprite templates directly from disk instead of requiring them to be loaded in the file list
- Treat an unreadable or invalid same-name Sprite as an error instead of silently overwriting it with a static Sprite
- Use only the valid frame count and known magic variant from the template
- Rebuild a canonical RGBA v31 header from the PNG instead of copying the template header or encoding
- Support v31, v61, BPP 0, and malformed-dimension templates when their frame count is valid
- Reject non-divisible PNG widths before modifying the destination
- Verify the completed temporary Sprite before replacing the destination
- Replace existing PNG and Sprite outputs through a backup-safe commit path instead of deleting them first
- Updated frame-template status text and conversion guidance
- Changed the default magic for newly created one-frame Sprites from `SpA1` to the vanilla-compatible `SPa1`; template-based conversions still preserve the template's known magic variant

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
