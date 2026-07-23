# D2R Sprite Toolkit v4.0.2

## Important Fix

Version 4.0.2 replaces the PNG-to-Sprite frame-template path introduced in v4.0.1. Updating is strongly recommended for anyone who converts animated or multi-frame PNG files.

## Changes

- Same-name Sprite templates are now detected directly from disk, even when they are not loaded in the application list.
- The template contributes only its valid frame count and known Sprite magic variant.
- Template dimensions, version, encoding, BPP, payload-size fields, and other header values are no longer copied.
- The output is rebuilt as a canonical RGBA v31 Sprite using the PNG dimensions.
- Valid templates with BPP 0 are accepted.
- v61/DXT5 templates can provide the frame count, while the new output remains v31/RGBA.
- Incorrect template width metadata no longer blocks conversion.
- If a same-name Sprite exists but is unreadable or has an invalid frame count, conversion stops without overwriting it.
- If the PNG width cannot be divided evenly by the frame count, conversion stops without modifying the existing Sprite.
- Completed temporary output is verified before a backup-safe replacement of an existing file.
- Newly created one-frame Sprites now use the vanilla-compatible `SPa1` magic by default. Template-based conversions continue to preserve a recognized `SPa1` or `SpA1` variant from the template.

## Example

A `921 x 61` PNG with a same-name Sprite containing `3` frames is rebuilt as:

- Frame count: `3`
- Frame width: `307`
- Total width: `921`
- Height: `61`
- Output encoding: `RGBA v31`

The template may report BPP `0`, incorrect dimensions, or version `61`; those values are not copied into the output.

## Installation

1. Download `D2RSpriteToolkit_v4.0.2_Windows.zip`.
2. Extract the archive.
3. Run `D2RSpriteTK.exe`.

No installer is required.
