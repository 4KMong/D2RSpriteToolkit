# D2R Sprite Toolkit v4.0.1

## Fixed

- PNG-to-Sprite conversion no longer requires the PNG dimensions to match incorrect dimensions stored in a frame-template Sprite.
- When a same-name frame-template Sprite is found, the converter now uses its frame count and recalculates the frame width and image-related header values from the PNG.
- Conversion stops only when the PNG width cannot be divided evenly by the template frame count.

Example: a 7,920-pixel-wide PNG with a 60-frame template is rebuilt as 60 frames with a width of 132 pixels per frame, even if the original template incorrectly reports a total width of 7,919 pixels.

## Installation

1. Download `D2RSpriteToolkit_v4.0.1_Windows.zip`.
2. Extract the ZIP file.
3. Run `D2RSpriteTK.exe`.

## Notes

- Windows only
- No installer is required
- Windows SmartScreen may display a warning because the executable is not code-signed
- Back up original files before processing them
- This is an unofficial fan-made utility and is not an official Blizzard Entertainment product
