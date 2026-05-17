# OstranautsTranslator

Language / 语言: [English](README.md) | [简体中文](README.zh-CN.md)

OstranautsTranslator is a Simplified Chinese package for Ostranauts.

It is meant for players first: download the release archive, extract it into the game folder, and start the game.

## What It Includes

- the required BepInEx runtime files
- doorstop startup files
- the OstranautsTranslator plugin
- the Chinese mod files
- the translation data and runtime settings used by the package

## Installation

1. Close Ostranauts.
2. Open the latest release page.
3. Download `OstranautsTranslator-v*.zip`.
4. Extract the archive directly into the game root, the folder that contains `Ostranauts.exe`.
5. Overwrite existing files when prompted.
6. Start the game.

## Basic Use

- In normal use, you only need to launch the game after installation.
- Press `F6` in game if you want to open the translator status window.
- If the game updates and the translation looks outdated, run `OstranautsTranslator.exe` once from the `OstranautsTranslator` folder, then launch the game again.

## Updating

To update to a newer version:

1. Close the game.
2. Download the newer release zip.
3. Extract it into the same game root.
4. Overwrite existing files.

## Uninstall

Remove these paths from the game directory if you want to uninstall the package:

- `BepInEx`
- `doorstop_config.ini`
- `winhttp.dll`
- `OstranautsTranslator`
- `Ostranauts_Data\Mods\OstranautsTranslate`

If you already had your own BepInEx setup before installing this package, remove only the files that came from this release.

## Troubleshooting

- The translation does not load:
  Make sure the zip was extracted into the folder that contains `Ostranauts.exe`, not into a nested subfolder.
- The game updated and some text is wrong or missing:
  Run `OstranautsTranslator.exe` once, then restart the game.
- You want to verify the package is active:
  Start the game and press `F6`. If the status window opens, the plugin is loaded.

## Releases

Use the GitHub Releases page to download the latest packaged build for players.