# Releasing Ragnavik UI

## Release requirements

1. Update the version in `package/manifest.json` and the `BepInPlugin` attribute in `src/RagnavikUI.cs`.
2. Add the release notes to `package/CHANGELOG.md` and update `package/README.md` when behavior or requirements change.
3. Publish a corresponding release blog post on the Ragnavik website. Every Ragnavik UI release requires a website blog post. Do not publish the Thunderstore package until that post is ready.
4. Run `./scripts/validate.sh`.
5. Run `./scripts/package.sh` twice with the same managed assembly inputs and verify that the resulting archive checksum is unchanged.
6. Inspect the archive. It must contain only `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, the config asset, and the freshly built plugin DLL.
7. Tag the source commit with `v<version>` only after the release contents are final.

## Anti cheat coordination

Anti cheat policy is derived from the complete effective client and server manifests, not a partial or remembered mod list.

* Version check every shared client and server mod.
* Put every client only mod in `CatosAntiCheat_ExtraWhitelist.txt`. Ragnavik UI is client only, so `lostkode.ragnavik.ui` belongs there.
* Put every server only mod in `CatosAntiCheat_ServerOnly.txt`.
* Before a deployment, verify source and runtime plugin parity plus the live loaded plugin counts on both client and server where applicable.

Publishing this package does not authorize a live server restart or deployment.
