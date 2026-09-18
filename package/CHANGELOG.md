# Changelog


## 1.1.0

* Add an independently guarded main-menu changelog module.
* Reuse Valheim's native changelog button and panel.
* Read pack-owned text from `BepInEx/config/RagnavikUI/changelog.txt` and fall back to Valheim's changelog when it is unavailable.
* Add configuration for automatic opening and vanilla-text replacement.
## 1.0.3

* Remove the TrashItems dependency and trash click integration.
* Retire the inventory position override so each inventory mod controls its own layout.
* Preserve the Ragnavik compass and clock presentation.

## 1.0.2

* Place armor, gold, weight, and trash in four measured rows.
* Align the visible trash panel with its interactive drop target.
* Restore drag-to-delete behavior through TrashItems' existing action.
* Move the sidebar closer to the inventory edge and improve vertical spacing.
* Reapply layout after other inventory mods finish their updates.

## 1.0.1

* Move the full AzuClock display closer below HUDCompass.
* Add the first Ragnavik inventory sidebar layout correction.
