# Ragnavik UI

Client UI modules for the Ragnavik Valheim pack. The plugin adds a separate guarded Ragnavik changelog button and panel without changing Valheim's own changelog. AzuClock's full clock, day, time of day, and weather display sits below HUDCompass.

Test Mode keeps character selection local, then automatically launches the on-system `galetest1` world for rapid development testing. Production builds route Play Ragnavik through character selection and then connect to the configured remote Ragnavik server.

The Ragnavik panel loads updates from the public Ragnavik website and caches the last successful response for offline use. The main menu also provides configurable Discord help and Buy Me a Coffee support links.

The Ragnavik character selector shows only characters created inside the active Ragnavik production or local test environment. It never imports, renames, moves, or displays existing Valheim characters. Ragnavik characters remain in Valheim's normal protected character storage so native save, backup, cloud, and recovery behavior continues to apply.

If the active environment has no registered character, Ragnavik leaves the selector empty and requires creating one before entry. Test characters never appear in Production, and Production characters never appear in Test.

Install this package through Ragnavik 1.1.12 or newer. Its plugin is client only. Ragnavik Server 1.0.6 or newer allows the plugin ID `lostkode.ragnavik.ui` in the anti-cheat profile; the dedicated server does not need this UI plugin.

## Changelog

### 1.1.0

* Add a separate Ragnavik Updates button and changelog panel.
* Load authoritative updates from the Ragnavik website with a local cache.
* Leave Valheim's native changelog unchanged.
* Add Ragnavik branding and community links to the main menu.

See `CHANGELOG.md` for the complete release history.
