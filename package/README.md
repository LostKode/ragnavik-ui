# Ragnavik UI

Client UI modules for the Ragnavik Valheim pack. The plugin adds a guarded Ragnavik changelog to Valheim's native main-menu panel. AzuClock's full clock, day, time of day, and weather display sits below HUDCompass.

The changelog reads `BepInEx/config/RagnavikUI/changelog.txt`. If that pack-owned file is missing or empty, Valheim's normal changelog remains available.

Install this package through Ragnavik 1.1.12 or newer. Its plugin is client only. Ragnavik Server 1.0.6 or newer allows the plugin ID `lostkode.ragnavik.ui` in the anti-cheat profile; the dedicated server does not need this UI plugin.

## Changelog

### 1.1.0

* Add an independently guarded main-menu changelog module.
* Reuse Valheim's native changelog button and panel.
* Fall back safely to Valheim's changelog when Ragnavik content is unavailable.

See `CHANGELOG.md` for the complete release history.
