# D2R Sprite Toolkit

D2R Sprite Toolkit is an unofficial, free, fan-made Windows utility for PNG and Sprite conversion in Diablo II: Resurrected modding workflows.

It combines low-resolution PNG generation, Sprite preview and conversion, and batch file renaming in a single portable application.

> This project is not affiliated with, endorsed by, approved by, or sponsored by Blizzard Entertainment.

## Features

- Convert PNG files to `.lowend.png` at 50% size by default
- Force a custom output width and height
- Reuse the dimensions of an existing `.lowend.png` in the same directory
- Preview `.sprite` files and inspect their metadata
- Convert Sprite files to PNG
- Convert PNG files to static Sprite files
- Use a same-name frame Sprite as a template to preserve its header and frame structure
- Batch rename files with prefixes, suffixes, text replacement, and partial deletion
- Drag and drop files or directories
- Korean and English user interfaces
- Minimize to the system tray
- Prevent multiple instances from running at the same time
- Build as a single Windows executable

## Requirements

- Windows 10 or Windows 11
- No installer is required
- The downloadable build is distributed as a single executable

## Download

Download the latest Windows package from the repository's **Releases** page, extract the ZIP file, and run `D2RSpriteTK.exe`.

Additional distribution pages:

- Nexus Mods: https://www.nexusmods.com/diablo2resurrected/mods/1144
- Inven Mod Archive: https://www.inven.co.kr/board/diablo2/5842/7796

## Basic Usage

### Create low-resolution PNG files

1. Drag PNG files or a directory into the application.
2. The default output size is 50% of the source image.
3. Set a forced size or a custom suffix when needed.
4. Run the PNG-to-lowend conversion.

### Convert Sprite to PNG

1. Load a `.sprite` file.
2. Review the preview and metadata.
3. Run the Sprite-to-PNG conversion.
4. When the output-directory option is enabled, converted files are saved to `output_png`.

### Convert PNG to Sprite

1. Load a PNG file.
2. If a same-name frame Sprite exists in the same directory, it is used as a template automatically.
3. Without a template, the PNG is converted to a static Sprite.
4. Run the PNG-to-Sprite conversion.

Always keep a separate backup of the original files. Compatibility may vary depending on game updates and the structure of the target resource.

## Automated Build

The authoritative build definition is `.github/workflows/build.yml`.

GitHub Actions builds the application on a Windows runner, embeds all required resources, and produces:

```text
D2RSpriteToolkit_v4.0.0_Windows.zip
```

The package contains:

```text
D2RSpriteTK.exe
LICENSE
NOTICE.md
RELEASE_NOTES_v4.0.0.md
```

No BAT or local build wrapper is included because the GitHub Actions workflow performs the complete build and packaging process directly.

## Repository Structure

```text
D2RSpriteToolkit/
├─ .github/workflows/build.yml
├─ lang/
│  ├─ en.lng
│  └─ ko.lng
├─ Program.cs
├─ app.ico
├─ tray.ico
├─ logo.png
├─ loading.png
├─ app.manifest
├─ README.md
├─ CHANGELOG.md
├─ CONTRIBUTING.md
├─ LICENSE
├─ NOTICE.md
└─ RELEASE_NOTES_v4.0.0.md
```

## License

This project uses a source-available license that permits modification and non-commercial redistribution under specific attribution, version-identification, and change-disclosure requirements.

See [`LICENSE`](LICENSE) for the complete terms.

## Translation Contributions

Community translations are welcome.

To contribute a translation:

1. Copy `lang/en.lng`.
2. Rename it using an appropriate language code, such as `de.lng`, `fr.lng`, or `es.lng`.
3. Translate only the text after each `=` sign.
4. Do not modify translation keys or placeholders such as `{0}`.
5. Save the file as UTF-8.
6. Submit the translated file through a GitHub pull request or issue.

Please note that new language files must be reviewed and integrated into a future application release. Adding an `.lng` file alone does not automatically add the language to the application menu.

See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed instructions.

## Rights Notice

Diablo II: Resurrected and related names and trademarks are the property of their respective owners. This repository does not distribute original game data files owned by Blizzard Entertainment.
