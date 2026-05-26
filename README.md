# Yakuza 6 Localization Tool [![Static Badge](https://img.shields.io/badge/Download-light_green)](https://github.com/zSavT/Yakuza-6-Localization-Tool/releases)



This tool automates the extraction, translation, and injection of text (`.bin`) and texture (`.dds`) files for Yakuza 6, utilizing `reARMP` and `ParTool`.

- [Yakuza 6 Localization Tool ](#yakuza-6-localization-tool-)
  - [How to Use the Program](#how-to-use-the-program)
    - [Prerequisites](#prerequisites)
    - [Step 1: Extraction](#step-1-extraction)
    - [Step 2: Translation \& Editing](#step-2-translation--editing)
    - [Step 3: Recreation \& Injection](#step-3-recreation--injection)
    - [Advanced Usage (Command Line Arguments)](#advanced-usage-command-line-arguments)
    - [Using `config.json`](#using-configjson)
    - [Custom DB Mode](#custom-db-mode)
  - [Logical Flow \& Architecture](#logical-flow--architecture)
    - [1. `Pipeline.cs` - The Orchestrator](#1-pipelinecs---the-orchestrator)
    - [2. `PoConverter.cs` - The Text Parser](#2-poconvertercs---the-text-parser)
    - [3. `CmnTextManager.cs` - The Binary Scanner](#3-cmntextmanagercs---the-binary-scanner)
    - [4. The `dictionary.json` Structure](#4-the-dictionaryjson-structure)
  - [External Dependencies \& Credits](#external-dependencies--credits)


## How to Use the Program

### Prerequisites
- Ensure `reARMP.exe` and `ParTool.exe` are in the same folder as this tool.
- Ensure `dictionary.json` is present.

### Step 1: Extraction
1. Run the program.
2. Enter the main folder path of the game (e.g., `C:\SteamLibrary\steamapps\common\Yakuza 6 - The Song of Life`).
3. Choose **Option 1 (Extraction)**.
   - The tool will automatically unpack `data\ui.par`.
   - It will extract target `.bin` and `.dds` files, converting text into `.po` files using `reARMP`.
4. Once finished, you will find a new folder called `Yakuza 6 - Patch`. Your translation files will be in `Yakuza 6 - Patch\workspace`.

### Step 2: Translation & Editing
- Open the `.po` files in `workspace\data\db\e` with a translation editor (like Poedit).
- Edit the `.dds` textures in `workspace\data\ui.par.unpack\e\texture` with your preferred image editor.

### Step 3: Recreation & Injection
1. Run the program again.
2. Enter the game folder path and choose **Option 2 (Recreation)**.
   - The tool will inject your translations back into the `.json` files.
   - It will rebuild the `.bin` files via `reARMP`.
   - It will repack your modified textures and files back into a new `ui.par` via `ParTool`.
3. Grab the generated files from `Yakuza 6 - Patch\output` and copy them into your game folder to see your mods in-game!
4. **Note:** If any injected text in `.cmn` files exceeds the maximum byte limit, the tool will safely truncate it and log the detailed original vs translated byte-count info in a `warnings.txt` file inside the `Yakuza 6 - Patch` folder. Any pipeline errors will also be logged in an `errors.txt` file in the same location. A comprehensive dashboard will summarize the execution at the end!

### Advanced Usage (Command Line Arguments)
You can run the tool from the command line or via a `.bat` script using these options to automate the process:

- `-g "PATH"`, `--game-path "PATH"`: Specify the game folder path directly.
- `-e`, `--extract`: Auto-start Phase 1 (Extraction).
- `-r`, `--recreate`: Auto-start Phase 2 (Recreation).
- `-t`, `--skip-textures`: Skip extracting/repacking textures with ParTool (speeds up testing texts).
- `-l "CODE"`, `--lang "CODE"`: Change the Gettext header language (e.g., `en`, `es`, `fr`). Default is `it`.
- `-c`, `--clean-all`: At the end of Phase 2, delete all working folders (`og file`, `workspace`, etc.) keeping ONLY `output`.
- `-y`, `--yes`: Skip all Y/N confirmation prompts and auto-exit when done.
- `-q`, `--quiet`: Suppress output logs from external tools (reARMP, ParTool).
- `-ns`, `--no-split`: Disable automatic splitting of the `sound_auth.po` file during extraction.
- `-d "PATH"`, `--dict "PATH"`: Specify a custom dictionary file (default: `dictionary.json`).

**Example (.bat script):**
`PoConverter.exe -g "C:\Steam\steamapps\common\Yakuza 6" -r -t -y -d "my_dictionary.json"`

### Using `config.json`
Instead of passing arguments every time, you can edit the `config.json` file in the same folder as the tool. The program will read these defaults automatically:

```json
{
  "gamePath": "C:\\SteamLibrary\\steamapps\\common\\Yakuza 6 - The Song of Life",
  "language": "it",
  "dictionaryFile": "dictionary.json",
  "skipTextures": false,
  "cleanAll": false,
  "autoYes": true,
  "quietLogs": true,
  "splitSoundAuth": true,
  "custom-db": ""
}
```
*Arguments passed via the command line will override these defaults.*

### Custom DB Mode
If you need to process a specific set of `.bin` files located in a custom folder (for instance, a standalone database directory), you can specify its path under the `"custom-db"` key in `config.json`.

When `"custom-db"` is configured and has a value:
- The game path (`gamePath`) is completely bypassed (it is not required, requested, or validated).
- **Extraction (Phase 1)**: The tool will scan the custom folder recursively for `.bin` files, copy them to `og file`, extract them to `.json` with `reARMP.exe`, and convert their strings into `.po` translation files inside the `workspace` directory.
- **Recreation (Phase 2)**: The tool will inject modified `.po` files from the workspace into the `.json` files and compile them back to `.bin` via `reARMP.exe` into the `output` directory, skipping any `.par` archive extraction or repacking.




## Logical Flow & Architecture

### 1. `Pipeline.cs` - The Orchestrator
This class manages file routing and the execution of external tools (`ParTool.exe` and `reARMP.exe`).

- **Initialization**: Loads `dictionary.json` to create hash sets of allowed `.bin` files (texts) and `.dds` files (textures).
- **Extraction (Phase 1)**:
  1. Executes `ParTool.exe extract` to unpack `ui.par`, `talk.par`, and automatically any `.par` files found inside the `auth` folder.
  2. Scans the game directory for all files.
  3. Replicates the original folder structure in `og file` to preserve unmodified base data.
  4. If a file is in the dictionary, copies it to `workspace`.
  5. For `.bin` files, invokes `reARMP.exe` to generate a JSON, then calls `PoConverter.JsonToPo` to extract translatable strings into a `.po` file. For `.cmn.bin` files, it uses the internal binary scanner.
- **Recreation (Phase 2)**:
  1. Copies the entire original file tree from `og file` to `output`.
  2. Iterates over modified `.po` files in `workspace`, calling `PoConverter.PoToJson` to update the original JSONs.
  3. Runs `reARMP.exe` to compile the updated JSON into a new `.bin`, replaces the old `.bin` in `output`, and moves the updated `.json` to `converted json`.
  4. Copies modified `.dds` textures directly to `output` overriding readonly flags.
  5. Runs `ParTool.exe create` on the unpacked folders to build ready-to-use `.par` packages, then deletes the temporary folders.
  6. Cleans up the `output` folder by removing any `.bin` file not listed in the dictionary, ensuring only targeted mod files are deployed.

### 2. `PoConverter.cs` - The Text Parser
Handles bidirectional conversion between reARMP's JSON output and standard Gettext `.po` files.

- **`JsonToPo`**: Uses a recursive algorithm (`ExtractValues`) to navigate the JSON tree guided by wildcards (`*`) and paths defined in `dictionary.json`. Outputs the found strings into `msgctxt` (JSON path) and `msgid` (text), escaping special characters (`\r\n`).
- **`PoToJson`**: Reads the `.po` line by line. Features error-handling to prevent crashes on malformed blocks. It reconstructs multi-line strings, finds the JSON token via the path stored in `msgctxt`, and overwrites the text. Writes the output enforcing LF line-endings and 2-space indentation to strictly match the expected format of `reARMP`.

### 3. `CmnTextManager.cs` - The Binary Scanner
A custom raw binary scanner for `.cmn` cutscene files. It reads `.bin` files byte-by-byte to extract translatable strings without relying on external decompilers, automatically filtering out internal engine IDs, repetitive strings, and purely Japanese debug text.

To keep the extracted files clean of random binary noise, the scanner utilizes advanced heuristics:
- **Alphabet constraints**: Extracts only text using Latin, Western European accented, or Japanese characters, immediately discarding Cyrillic, Armenian, or other non-pertinent alphabets.
- **Vowel and ratio check**: Latin text must contain at least one vowel, and at least 50% of the characters must be letters.
- **Length and repetition exemptions**: Allows short 2-character words (if matching a whitelist, e.g., "no", "ok"), and exempts natural language strings from the repetition limit.
- **System text auto-tagging**: Single lowercase words are annotated in the `.po` files with a `#. WARNING` comment indicating they are likely untranslatable system identifiers (e.g., "substory", "kiryu").

During recreation, it safely injects the translated text back into the exact memory offset, truncating it at a valid UTF-8 character boundary if it exceeds the original byte limit to prevent file corruption.


### 4. The `dictionary.json` Structure
The dictionary file tells the tool which files to process and where to find the text inside the JSONs. It has two main sections:

```json
{
  "texts": {
    "file_name.bin.json": [
      ["path", "to", "text_node"],
      ["*", "wildcard_supported", "node"]
    ]
  },
  "textures": {
    "texture_name.dds": []
  }
}
```
- `texts`: The key is the JSON filename. The value is an array of paths (which are arrays of strings) pointing to the translatable strings. You can use `*` as a wildcard to iterate through all elements in an array or object.
- `textures`: The key is the DDS filename. The value is an empty array `[]`. It acts as a simple whitelist of textures to extract and repack.



## External Dependencies & Credits

- **[reARMP](https://github.com/Ret-HZ/reARMP)**: Script used to parse Yakuza `.bin` files into `.json` format and vice-versa. Credit to Ret-HZ.
- **[ParTool](https://github.com/Kaplas80/ParManager)**: Used to extract and compress `.par` archives. Credits to Kaplas80.
- **[Newtonsoft.Json](https://github.com/Newtonsoft/Json)**: Powerful .NET library for JSON manipulation.
