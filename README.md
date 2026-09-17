# Ragnavik UI

Ragnavik UI is a client only BepInEx plugin for the Ragnavik Valheim modpack. It layers layout fixes over AzuClock, HUDCompass, CurrencyPocket, and TrashItems. It does not replace those dependency mods or provide their features.

Version 1.0.3 restores the established version table changelog format. UI behavior and dependencies are unchanged.

## Install

Install `LostKode-Ragnavik_UI` through Thunderstore or as a dependency of the Ragnavik client pack. This plugin belongs only on clients. The dedicated server does not need it.

Because it is client only, add the plugin ID `lostkode.ragnavik.ui` to `CatosAntiCheat_ExtraWhitelist.txt` when CatosAntiCheat is enabled.

## Build

The build requires the managed Valheim assemblies and BepInEx core assemblies. Set both directories explicitly:

```sh
VALHEIM_MANAGED_DIR=/path/to/valheim_Data/Managed \
BEPINEX_CORE_DIR=/path/to/BepInEx/core \
./scripts/build.sh
```

Create the Thunderstore package with:

```sh
VALHEIM_MANAGED_DIR=/path/to/valheim_Data/Managed \
BEPINEX_CORE_DIR=/path/to/BepInEx/core \
./scripts/package.sh
```

The deterministic archive is written to `artifacts/LostKode-Ragnavik_UI-1.0.3.zip`. Repeated packaging from identical source and tool inputs produces identical archive bytes.

Run source and package metadata validation without game assemblies:

```sh
./scripts/validate.sh
```

See [docs/RELEASING.md](docs/RELEASING.md) for the release process.

## Source provenance

This repository was split from `LostKode/ragnavik` commit `770130924383801ca9fb66e0f9d9aa2bfb44f3c0`, which contains the published 1.0.2 source and assets. Generated binaries and caches were intentionally excluded.
