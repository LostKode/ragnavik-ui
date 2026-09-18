# Ragnavik Character Storage Investigation

## Decision

Separate physical character folders are not safe to implement at the Ragnavik UI layer with the current Valheim and ServerCharacters APIs.

Valheim 1.0 gives `PlayerProfile` a filename and a `FileHelpers.FileSource`, but no custom character root. `SaveSystem.GetCharacterFolderPath(FileSource)` maps that source to one of Valheim's fixed roots:

| Source | Character root |
| --- | --- |
| Local | `characters_local/` |
| Non-local character storage | `characters/` |

`PlayerProfile` save, load, removal, vanilla backup, restore, and enumeration all resolve files through those source-based roots. Redirecting the root for a Ragnavik profile would therefore require patching a shared save-system path method or maintaining fragile ambient context around every caller. Either approach can affect unrelated characters and violates the requirement to avoid broad path redirection.

No physical-folder implementation was made.

## Evidence inspected

### Installed versions

- Valheim managed assembly inspected from the current Steam installation: `assembly_valheim.dll`, SHA-256 `59f53fb55d99d22a33e8ed094eec8d21e9f133543bce92bc3d80dce44033adb1`.
- Gale cache package inspected: `Smoothbrain-ServerCharacters 1.4.17`.
- Installed `ServerCharacters.dll` SHA-256: `0b497b7e830365bc57d0a5ada200e9e571b0f3e3625ac08de06a2bc528850872`.
- Public ServerCharacters source at commit `bb7d3cd656a9a3b4fa761e02da6cec13c3325213` declares version `1.4.17` and matches the installed assembly's relevant types and method names.

### Valheim 1.0 storage behavior

Inspection of the current `assembly_valheim.dll` shows:

- `SaveSystem.GetCharacterFolderPath(FileSource)` calls `Utils.GetSaveDataPath(source)` and appends only `characters_local/` for local storage or `characters/` for the other supported character source.
- `SaveSystem.GetCharacterPath(FileSource, filename)` appends `filename + ".fch"` to that fixed folder.
- `SaveSystem.GetAllPlayerProfiles()` enumerates the character `SaveDataType` collection, constructs each `PlayerProfile` from its save name and primary file source, and calls `Load()`.
- `PlayerProfile.SavePlayerToDisk()` obtains its root from `SaveSystem.GetCharacterFolderPath(m_fileSource)`, then handles the primary `.fch`, `.fch.old`, and `.fch.new` files in that root.
- `PlayerProfile.LoadPlayerFromDisk()` and `LoadPlayerDataFromDisk()` load by the profile's filename and file source.
- `PlayerProfile.RemoveProfile()` delegates deletion to the save system by filename and source.
- Vanilla character creation lowercases the entered character name, constructs `PlayerProfile(filename, FileSource.Cloud)`, optionally switches it to local storage, sets the visible character name separately, saves player data, and writes the profile.
- Vanilla character selection caches `SaveSystem.GetAllPlayerProfiles()` in `FejdStartup.m_profiles`; selection, preview, deletion, and start all consume that exact list.

The separation between the profile filename and the visible name is useful for a safe fallback, but it does not provide a custom directory.

### ServerCharacters 1.4.17 behavior

The public 1.4.17 source and installed DLL show that ServerCharacters uses the same vanilla character root directly:

- `Utils.CharacterSavePath` is exactly `SaveSystem.GetCharacterFolderPath(FileSource.Local)`.
- Client emergency saves write `<profile.m_filename>.fch.signature` and `<profile.m_filename>.fch.serverbackup` directly under that root.
- Emergency restore and cleanup resolve those files from the same root and current profile filename.
- Client save interception patches `PlayerProfile.SavePlayerToDisk()` and uploads the serialized profile bytes once a server character is active.
- The server reconstructs its authoritative filename from platform identity plus the profile's visible name, then saves it with `FileSource.Local`.
- Server backup interception also patches `PlayerProfile.SavePlayerToDisk()`. It reads `<server filename>.fch.old` through `SaveSystem.GetCharacterFolderPath(profile.m_fileSource)` and stores its ZIP history under the same character root.
- Server-side discovery, migration, administration, and restore enumerate that root directly.

This means a client-only physical-folder redirect must also account for ServerCharacters emergency files. A broad redirect of `SaveSystem.GetCharacterFolderPath` risks changing vanilla profiles and unrelated character operations. A narrow redirect applied only during UI enumeration would leave later load, save, delete, backup, and ServerCharacters emergency operations pointed at a different location.

## Narrow safe alternative requiring approval

Keep every profile in Valheim's normal source-specific character root and isolate Ragnavik ownership by internal filename plus a small Ragnavik registry:

1. Give every newly created Ragnavik profile an opaque internal filename such as `ragnavik_prod_<id>` or `ragnavik_test_<id>` while keeping the player-entered visible name in `PlayerProfile.SetName()`.
2. Store a registry under the Ragnavik UI configuration directory keyed by internal filename, environment, and the saved `m_playerID` for validation.
3. Populate the Ragnavik selector only from profiles that both use the reserved filename prefix and have a matching registry record for the active environment.
4. Never register, rename, move, import, edit, or delete an existing unowned Valheim profile.
5. Create profiles through a dedicated Ragnavik creation path using the public `PlayerProfile` constructor, `SetName`, `SavePlayerData`, and `Save` behavior reflected by vanilla creation.
6. Hand the selected `PlayerProfile` to the direct-entry module through a minimal in-process selection event or read-only selection property. Keep addresses and routing out of this module.
7. Let vanilla `PlayerProfile` and `SaveSystem` operations retain full ownership of `.fch`, `.old`, `.new`, cloud/local behavior, deletion, and restore. Let ServerCharacters continue using the unchanged selected filename for client emergency artifacts and its own platform-ID-plus-visible-name server profile.

This alternative provides selector isolation and separate production/test namespaces without separate physical folders. It still requires Gale runtime verification before any character-safety claim, especially for creation, restart persistence, deletion, vanilla backup restore, cloud/local behavior, ServerCharacters acquisition, disconnect emergency backup, reconnect restore, and stable `m_playerID`.

## Approval gate

Do not implement the filename-and-registry fallback until Daniel explicitly approves the weaker storage boundary. Physical folder isolation remains rejected unless Valheim or ServerCharacters adds a supported per-profile storage-root API.
