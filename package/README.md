# Ragnavik UI

Client UI modules for the Ragnavik Valheim pack. The plugin adds a separate guarded Ragnavik changelog button and panel without changing Valheim's own changelog. AzuClock's full clock, day, time of day, and weather display sits below HUDCompass.

The Ragnavik panel loads updates from the public Ragnavik website and caches the last successful response for offline use.

Install this package through Ragnavik 1.1.12 or newer. Its plugin is client only. Ragnavik Server 1.0.6 or newer allows the plugin ID `lostkode.ragnavik.ui` in the anti-cheat profile; the dedicated server does not need this UI plugin.

## Changelog

### 1.1.0

* Add a separate Ragnavik Updates button and changelog panel.
* Load authoritative updates from the Ragnavik website with a local cache.
* Leave Valheim's native changelog unchanged.

See `CHANGELOG.md` for the complete release history.
