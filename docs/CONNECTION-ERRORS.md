# Player connection errors

The connection dialog explains mod mismatch, game-version mismatch, server downtime, maintenance, authentication, bans, full servers, and platform restrictions. Mod mismatches direct players to the server-required Client Pack, explain that a newer pack may precede server deployment, and discourage independent mod updates. Original rejection details remain visible with rich-text delimiters escaped.

In BepInEx configuration, set `[Ragnavik Connection] Status Endpoint` to the deployed public HTTPS `/connection-status` route from the status service. It defaults to empty because no public deployment route is currently configured. Do not include credentials. See the status-bot branch's PLAYER-CONNECTION-STATUS.md for the API and activation steps. Endpoint configuration must ship with the coordinated Client Pack release for players to receive live version notices without manual setup.

Requests run asynchronously at connection preparation and failure, time out after five seconds, reject redirects, and ignore stale responses or responses from an earlier attempt. A response must use schemaVersion 1 and a checkedAt Unix timestamp no more than 120 seconds old or 30 seconds ahead. Failure to fetch status preserves native error guidance and states that the required version could not be verified. Test mode does not request remote status. Current and last-verified client versions are labeled separately; published package metadata is never treated as deployment evidence.

Example: `Server-required Ragnavik client pack: 1.1.60. Use the exact client pack required by the server. If your pack is newer, wait for the server update or install the server-required version in Gale.` The version is supplied by the status service, not hardcoded in the plugin.

Build and automated message tests do not substitute for in-game validation of the dialog. Before publication, verify reachable, timeout, maintenance, older/newer pack, rejection-details, and retry flows in the authorized test setup.
