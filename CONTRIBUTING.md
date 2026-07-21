# Contributing to D2R Sprite Toolkit

> **Need a faster response?**
>
> GitHub issues and pull requests may not receive an immediate response.
>
> The fastest way to contact the author is to send a direct message through the D2R Sprite Toolkit page on Nexus Mods:
>
> https://www.nexusmods.com/games/diablo2resurrected/mods/1144

Thank you for your interest in contributing to D2R Sprite Toolkit.

Contributions may include bug reports, documentation improvements, translations, and code fixes.

For major changes or new features, please open an issue first and describe the proposed change before writing code.

## Bug Reports

When reporting a bug, please include:

- The application version
- Your Windows version
- A clear description of the problem
- Steps required to reproduce it
- The expected result
- The actual result
- Screenshots or sample files, when relevant

Do not include copyrighted game files unless they are necessary and you have permission to share them.

## Translation Contributions

Community translations are welcome.

The English language file should be used as the translation source:

    lang/en.lng

### Creating a Translation

1. Copy `lang/en.lng`.
2. Rename the copied file using an appropriate language code.
3. Translate only the text after each `=` sign.
4. Save the file as UTF-8.
5. Submit the translated file through a pull request or issue.

Example file names:

- `de.lng` for German
- `fr.lng` for French
- `es.lng` for Spanish
- `it.lng` for Italian
- `ja.lng` for Japanese
- `pl.lng` for Polish
- `pt-BR.lng` for Brazilian Portuguese
- `ru.lng` for Russian
- `zh-CN.lng` for Simplified Chinese
- `zh-TW.lng` for Traditional Chinese

### Translation Example

Original English entries:

    status.ready=Ready
    error.read_failed=Failed to read the file: {0}

German translation:

    status.ready=Bereit
    error.read_failed=Die Datei konnte nicht gelesen werden: {0}

### Translation Rules

- Do not change the keys before `=`.
- Translate only the values after `=`.
- Preserve placeholders such as `{0}`, `{1}`, and `{2}`.
- Preserve escape sequences such as `\n`.
- Preserve accelerator markers such as `&`, when present.
- Do not add or remove entries unless requested.
- Do not modify unrelated files.
- Do not modify `Program.cs` for a translation-only contribution.
- Use `lang/en.lng` as the source instead of translating from another language.
- Save the completed file as UTF-8.

### Submitting a Translation

Translations may be submitted in either of these ways:

1. Open a pull request containing the new `.lng` file.
2. Open an issue and attach the completed `.lng` file.

Please include:

- Language name
- Language code
- Translator name or GitHub username
- Whether the translation is complete
- Any strings that require additional context

Adding an `.lng` file alone does not automatically add the language to the application menu.

New languages must be reviewed and integrated into a future application release.

## Pull Requests

Keep each pull request focused on one specific purpose.

A pull request should:

- Clearly describe the change
- Avoid unrelated formatting or code changes
- Preserve existing features unless the change specifically addresses them
- Include testing information
- Reference a related issue when applicable

Translation-only pull requests should normally contain only the new or updated `.lng` file.

## Code Contributions

Before submitting a significant code change:

1. Open an issue describing the problem or proposed feature.
2. Wait for confirmation before implementing major structural changes.
3. Keep the implementation limited to the agreed scope.
4. Avoid adding unrelated features or dependencies.
5. Verify that existing PNG and Sprite conversion features still work.

The project prioritizes compatibility, reliability, and preservation of existing behavior.

## Review

All contributions are reviewed before they are merged.

A contribution may be:

- Accepted
- Revised
- Deferred
- Declined

Submitting a pull request does not guarantee that the change will be included.

## License

By submitting a contribution, you confirm that you have the right to provide the submitted material and agree that it may be distributed under the repository's license.
