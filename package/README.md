# Ragnavik UI

Client UI modules for the Ragnavik Valheim pack. The plugin adds original Ragnavik loading screens for game startup and initial world or server entry. It also adds a separate guarded Ragnavik changelog button and panel without changing Valheim's own changelog. AzuClock's full clock, day, time of day, and weather display sits below HUDCompass.

Test Mode keeps character selection local, then automatically launches the on-system `galetest1` world for rapid development testing. Production builds route Play Ragnavik through character selection and then connect to the configured remote Ragnavik server.

The Ragnavik panel loads updates from the public Ragnavik website and caches the last successful response for offline use. The main menu also provides configurable Discord help and Buy Me a Coffee support links. Existing main menu and changelog behavior remains unchanged by the loading screen module.

The Ragnavik character selector shows only characters created inside the active Ragnavik production or local test environment. It never imports, renames, moves, or displays existing Valheim characters. Ragnavik characters remain in Valheim's normal protected character storage so native save, backup, cloud, and recovery behavior continues to apply.

If the active environment has no registered character, Ragnavik leaves the selector empty and requires creating one before entry. Test characters never appear in Production, and Production characters never appear in Test.

Install this package through Ragnavik 1.1.12 or newer. Its plugin is client only. Ragnavik Server 1.0.6 or newer allows the plugin ID `lostkode.ragnavik.ui` in the anti-cheat profile; the dedicated server does not need this UI plugin.

Loading images and tips are packaged with Ragnavik UI and require no player configuration. Each loading event chooses a different image and tip when more than one is available. Images cover the complete screen while preserving their original aspect ratio, cropping overflow when needed. The textless Fjord Gate mark provides a subtle activity pulse during startup. Missing or incompatible loading assets leave Valheim's native presentation available.

Ragnavik UI does not replace teleport loading screens. TargetPortal and Valheim retain ownership of that flow.

## Changelog

### 1.2.5

* Add randomized loading art and tips for startup and initial world or server entry.
* Replace the supported startup loading spinner with the animated textless Fjord Gate mark.
* Fill the complete screen and keep the activity mark visible above the tooltip during startup and world entry.
* Lower the activity mark by five percent of screen height.
* Keep the world-entry activity mark on a dedicated top layer matching startup.
* Always show the same activity mark during the menu-to-world transition.
* Preserve vanilla fallbacks and leave teleport loading screens unchanged.
* Package all eight loading images and all 44 tips with Ragnavik UI.

See `CHANGELOG.md` for the complete release history.
