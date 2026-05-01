# OstranautsTranslator

`OstranautsTranslator.exe` is a command-line tool deployed into the `Ostranauts` game directory. It is used to:

- scan game text and build the workspace database
- export source text for translation and import external translation results
- export runtime translation text and import runtime miss reports
- call DeepSeek to translate glossary terms and body text in batches
- export or deploy the in-game translation mod

## Where to Run It

- After a solution build, the tool is copied to: `<game root>\OstranautsTranslator\`
- Run `OstranautsTranslator.exe` directly from that directory
- The tool automatically treats the **parent directory of the exe directory** as the game root

The fixed paths and identifiers are:

- Game root: `<exe dir>\..`
- Workspace: `<game root>\OstranautsTranslator\workspace`
- Main database: `<game root>\OstranautsTranslator\workspace\corpus.sqlite`
- Native mod output root: `<game root>\Ostranauts_Data\Mods`
- Native mod ID: `OstranautsTranslate`
- Native mod name: `OstranautsTranslate`

Running the exe with no arguments starts the automatic refresh workflow.

`-t` accepts three forms:

- `-tzh`
- `-t=zh`
- `-t zh`

The examples below use the preferred short form.

## Automatic Refresh

1. scan
2. generate
3. glossary
4. translate
5. deploy

When glossary regeneration is needed, the automatic workflow runs the bundled `generate_generic_glossary.py` script through `py` or `python`, so Python must be available on the machine.

If you want to force the same five-step pipeline even when the recorded version already matches the deployed mod, run:

- `OstranautsTranslator.exe rebuild`
- `OstranautsTranslator.exe rebuild -tzh`

To list the supported target languages, run:

- `OstranautsTranslator.exe -l`

The output shows the CLI argument name, the English language name, and the native language name.

## Build

Run this from the repository root:

- `msbuild .\OstranautsTranslator.sln /p:Configuration=Release /nologo /verbosity:quiet /clp:Summary`

By default, the solution build will:

- copy the plugin DLLs and `notosans` into `BepInEx\plugins\OstranautsTranslator\`
- copy `OstranautsTranslator.exe` and its dependencies into `<game root>\OstranautsTranslator\`
- copy the repository-root `config-example.ini` into the build output and the deployed exe directory
- copy the repository-root `config.ini` into the build output and the deployed exe directory if it exists
- copy `tmp\generate_generic_glossary.py` into the build output and the deployed exe directory as `generate_generic_glossary.py`

At runtime, the plugin reads the real game version shown in the UI from the game's Unity resource `Resources/version` and stores it in `BepInEx\config\OstranautsTranslator.cfg`. If the version changes, the plugin warns you in the log and status window to run `OstranautsTranslator.exe` again and refresh the translation. After a game update, it is best to launch the game once so the plugin can record the new version before you export or deploy again.

## Configuration File

The repository root includes a committed template file: `config-example.ini`.

Usage:

1. Copy `config-example.ini` to `config.ini`
2. Change only `[LLMTranslate] ApiKey`
3. Keep `config.ini` in the repository root and run `msbuild`
4. The build copies `config.ini` next to the deployed exe automatically

### INI Sections

`config.ini` uses these three sections:

- `[LLMTranslate]`: DeepSeek endpoint and shared request parameters
- `[TranslateGlossary]`: glossary translation prompt and switches
- `[TranslateLlm]`: body-text translation prompt and switches

Notes:

- `BatchSize`, `Temperature`, `MaxTokens`, and model/endpoint values live under `[LLMTranslate]`
- `SystemPrompt` must be configured separately in `[TranslateGlossary]` and `[TranslateLlm]`

## Workspace Layout

The tool uses this directory layout:

- `workspace\corpus.sqlite`: main database containing source text and translation tables
- `workspace\reference\glossary.json`: generic glossary
- `workspace\reference\glossary-en-to-<to>.json`: target-language glossary
- `workspace\exports\source\`: exported source text
- `workspace\imports\translations\`: imported body-text translations
- `workspace\imports\runtime-miss\`: imported runtime miss reports
- `workspace\exports\runtime\`: exported runtime text

The main database tables are:

- `native_mod_source`
- `runtime_source`
- `translate_<lang>`

## Commands

All commands below assume you are running the deployed `OstranautsTranslator.exe` from the game directory.

### List Supported Languages

- `OstranautsTranslator.exe -l`

This prints:

- CLI argument name
- English language name
- Native language name

### Scan Game Text

Use this to rebuild or refresh the source data in `workspace\corpus.sqlite`.

- `OstranautsTranslator.exe scan`

### Generate the Generic Glossary

Use this to run the bundled `generate_generic_glossary.py` helper through the exe.

- `OstranautsTranslator.exe generate`

### Force a Full Rebuild

Use this to run the full refresh pipeline unconditionally, without waiting for a version mismatch.

- `OstranautsTranslator.exe rebuild`
- `OstranautsTranslator.exe rebuild -tzh`

Supported parameter:

- `-t<lang>`

This command always runs the same five stages as the automatic workflow:

1. scan
2. generate
3. glossary
4. translate
5. deploy

### Export Source Text

Use this to export JSONL for an external translation workflow or model.

- `OstranautsTranslator.exe source -tzh`

Supported parameter:

- `-t<lang>`: target language; defaults to the system language when omitted

This command always includes already translated entries.

Export path:

- `workspace\exports\source\source-en-to-<to>.jsonl`

### Import External Translations

Use this to write external JSONL translation results back into the database.

- `OstranautsTranslator.exe import -tzh`

Supported parameter:

- `-t<lang>`

Default import path order:

1. `workspace\imports\translations\translations-en-to-<to>.jsonl`
2. If that file does not exist, fall back to `workspace\exports\source\source-en-to-<to>.jsonl`

The import JSONL format uses these locator fields:

- `source_kind`
- `source_id` or `source_key`

Imported records are written as final translations and existing translations are overwritten.

### Export Runtime Text

Use this to generate runtime translation text files.

- `OstranautsTranslator.exe runtime -tzh`

Supported parameter:

- `-t<lang>`

Fixed export path:

- `workspace\exports\runtime\<to>\Text\_AutoGeneratedTranslations.txt`

This command always includes draft translations.

### Import Runtime Misses

Use this to write runtime miss reports back into the database.

- `OstranautsTranslator.exe runtime-miss -tzh`

Supported parameter:

- `-t<lang>`

Default import path order:

1. `workspace\imports\runtime-miss\<to>\`
2. If that directory does not exist, fall back to `workspace\imports\runtime-miss\`

Imported runtime misses are always stored as final translations and overwrite existing entries.

### Export the Native Mod

Use this to export the translation stored in the database as a game mod.

- `OstranautsTranslator.exe native-mod -tzh`

Supported parameter:

- `-t<lang>`

`mod_info.json` uses the real game version recorded by the runtime plugin whenever available. This is the same version shown in the game UI, not the Unity engine version.

This command always includes draft translations and always verifies source hashes against the most recent scan.

### Deploy the Current Translation

Use this to deploy the current language translation from the database directly into the game mod directory.

- `OstranautsTranslator.exe deploy`
- `OstranautsTranslator.exe deploy -tzh`

Supported parameter:

- `-t<lang>`

Unlike the no-argument auto-refresh workflow, `deploy` performs only the explicit deployment step.

## DeepSeek Batch Translation

### Glossary Files

- Generic glossary: `workspace\reference\glossary.json`
- Target-language glossary: `workspace\reference\glossary-en-to-<to>.json`

`glossary` translates the generic glossary into a target-language glossary. `translate` prefers the target-language glossary when translating body text in batches.

Both commands now have separate system-prompt settings:

- `glossary` uses `[TranslateGlossary] SystemPrompt`
- `translate` uses `[TranslateLlm] SystemPrompt`

The built-in defaults are opinionated toward:

- personal names: stable transliteration
- other player-facing terms: semantic translation whenever the meaning is interpretable
- IDs, serials, model numbers, hotkeys, and obvious technical identifiers: preserved unchanged

### Generate the Generic Glossary

`workspace\reference\glossary.json` is not generated by `scan`. The simplest way to prepare it is:

- `OstranautsTranslator.exe generate`

Internally this calls the bundled helper script:

- `generate_generic_glossary.py`

The solution build copies this script next to `OstranautsTranslator.exe`, so you can run it directly from the deployed tool directory. The script scans `Ostranauts_Data\StreamingAssets\data`, extracts candidate English proper nouns from JSON and TSV files, and writes:

- `workspace\reference\glossary.json`

The no-argument auto-refresh workflow runs this bundled script for you automatically.

If you want to call the script directly instead of `generate`, the simplest usage from the deployed tool directory is:

- `python .\generate_generic_glossary.py`

If the script's default absolute paths do not match your machine, you can pass two positional arguments explicitly: the game data root and the output file path.

- `python .\generate_generic_glossary.py "<game root>\Ostranauts_Data\StreamingAssets\data" "<game root>\OstranautsTranslator\workspace\reference\glossary.json"`

The output `glossary.json` is a JSON object grouped by category. The current keys are:

- `people`
- `places`
- `entities`
- `ships`
- `items`

Example:

```json
{
  "people": ["Gideon Pruitt", "Camellia Middlemist"],
  "places": ["K-Leg", "Nnamdi Azikiwe Station"],
  "entities": ["Ayotimiwa", "GalCon"],
  "ships": ["Mimas"],
  "items": ["Black Bull Kape"]
}
```

The script uses heuristic extraction, so it is not guaranteed to be perfect. After generation, review the file manually:

- remove common words that slipped in
- add missing proper nouns
- normalize spelling for names, places, and brands

### Translate the Glossary Only

- `OstranautsTranslator.exe glossary -tzh`

### Translate Body Text with DeepSeek

- `OstranautsTranslator.exe translate -tzh`

### Test One Body-text Batch

- `OstranautsTranslator.exe test -tzh`

`test` uses the same body-text translation settings as `translate`, but it stops after one completed body-text batch and saves that batch to the translation database immediately. This is useful for checking prompt quality and glossary behavior before you launch a long full translation run.

`translate` reads these values from `config.ini`:

- `[LLMTranslate]`: `ApiKey`, `Url`, `Model`, `Temperature`, `MaxTokens`, `BatchSize`
- `[TranslateGlossary]`: `SystemPrompt`, `OverwriteExisting`
- `[TranslateLlm]`: `SystemPrompt`, `TranslationState`, `Translator`, `TranslateGenericGlossaryFirst`, `RefreshGlossary`, `OverwriteExisting`, `IncludeDraft`

When `translate` needs to auto-generate the target-language glossary first, it uses the glossary prompt from `[TranslateGlossary] SystemPrompt` for that glossary phase, then switches back to `[TranslateLlm] SystemPrompt` for body-text batches.

Default behavior:

- if `glossary-en-to-<to>.json` already exists, it is used directly for body-text translation
- if the target-language glossary does not exist but `glossary.json` does, `translate` generates the target-language glossary first and then submits body-text batches
- LLM batch submission progress is shown with a progress bar
- each completed body-text batch is written to the translation database immediately, so rerunning `translate` after an interruption continues from the remaining untranslated entries when `OverwriteExisting=false`
- `test` runs the same selection logic but stops after the first completed body-text batch and writes that batch to the database immediately

### Recommended Workflow

1. Copy `config-example.ini` to `config.ini` and set your own `ApiKey`
2. `msbuild .\OstranautsTranslator.sln /p:Configuration=Release /nologo /verbosity:quiet /clp:Summary`
3. Launch the game once after an update so the plugin can record the current UI version
4. Run `OstranautsTranslator.exe`

If you want to force a full rebuild regardless of version state, run:

4. Run `OstranautsTranslator.exe rebuild`

If you want the manual workflow instead of the automatic version check, use:

1. `OstranautsTranslator.exe scan`
2. `OstranautsTranslator.exe generate`
3. `OstranautsTranslator.exe glossary -tzh`
4. `OstranautsTranslator.exe translate -tzh`
5. `OstranautsTranslator.exe deploy -tzh`

## Bracket Token Rules

For bracket token rules such as `[us]`, `[them]`, `[3rd]`, `[protag]`, `[contact]`, and `[target]`, see:

- `docs/bracket-token-translation-rules.md`

If you need to adjust how `scan`, `source`, or `translate` handles those tokens, treat that document as the source of truth.
