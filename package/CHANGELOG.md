# Changelog

| Version | Changes |
| --- | --- |
| 1.2.4 | Add branded full-screen loading art and randomized tips across startup, menu-to-world transition, and initial world entry. Replace visible vanilla loaders with the animated Fjord Gate activity mark at the approved position, keep it above tips throughout each phase, and leave teleport loading screens under their existing owner. Package eight loading images and 44 tips. |
| 1.2.3 | Show only the character's chosen name in the Ragnavik selector while retaining the private environment-specific storage identifier used to isolate Test and Production characters. |
| 1.2.2 | Refresh the filtered character screen immediately so an environment with no matching character opens on the new-character state instead of showing a stale preview. |
| 1.2.1 | Block entry when no environment-owned character is selected, require character and direct-entry environments to match, and integrate the protected direct-entry flow. |
| 1.2.0 | Isolate production and local test character selection with Ragnavik-owned filenames and identity-checked registries while leaving existing Valheim characters untouched. |
| 1.1.1 | Update the AzuClock dependency to 1.1.1. |
| 1.1.0 | Add a separate Ragnavik changelog button and panel without changing Valheim's changelog. Load the authoritative changelog from the Ragnavik website and cache the last successful response for offline launches and temporary website failures. Add the Fjord Gate menu logo, Discord help link, and Buy Me a Coffee support link. Remove the Valheim mod warning and merch link. Add configuration for automatic changelog opening, the public endpoint, and community link destinations. Remove the direct CurrencyPocket dependency because Ragnavik UI no longer adjusts its layout. |
| 1.0.4 | Ship the UI package with the approved Fjord Gate icon. |
| 1.0.3 | Remove the TrashItems dependency and trash click integration. Retire the inventory position override so each inventory mod controls its own layout. Preserve the Ragnavik compass and clock presentation. |
| 1.0.2 | Place armor, gold, weight, and trash in four measured rows. Align the visible trash panel with its interactive drop target. Restore drag to delete behavior through TrashItems' existing action. Move the sidebar closer to the inventory edge and improve vertical spacing. Reapply layout after other inventory mods finish their updates. |
| 1.0.1 | Move the full AzuClock display closer below HUDCompass. Add the first Ragnavik inventory sidebar layout correction. |
