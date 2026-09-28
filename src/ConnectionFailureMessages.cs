using System;

namespace RagnavikUI;

internal enum NativeConnectionStatus
{
    None = 0,
    Connecting = 1,
    Connected = 2,
    ErrorVersion = 3,
    ErrorDisconnected = 4,
    ErrorConnectFailed = 5,
    ErrorPassword = 6,
    ErrorAlreadyConnected = 7,
    ErrorBanned = 8,
    ErrorFull = 9,
    ErrorPlatformExcluded = 10,
    ErrorCrossplayPrivilege = 11,
    ErrorKicked = 12
}

internal static class ConnectionFailureMessages
{
    internal const string CatosRejectionPrefix = "Connection rejected:";

    internal static string Format(int nativeStatus, string? catosRejection, ServerConnectionStatus? server = null)
    {
        // Availability guidance is relevant to transport and mod failures, not account errors.
        bool useServerStatus = !string.IsNullOrWhiteSpace(catosRejection) ||
            nativeStatus is 3 or 4 or 5 or 12;
        string context = "";
        if (useServerStatus && server != null && server.IsFresh(DateTimeOffset.UtcNow))
        {
            if (server.RequiredVersion != null)
                context = $"Server-required Ragnavik client pack: {server.RequiredVersion}.\n\n";
            else if (server.LastVerifiedVersion != null)
                context = $"Last verified Ragnavik client pack: {server.LastVerifiedVersion}. The current server requirement is not confirmed.\n\n";
            if (server.state == "maintenance")
                return "The Ragnavik server is undergoing maintenance.\n\n" + context +
                    "Wait for the server to return, then try again. A newly published client pack may be available before the server update is ready.";
            if (server.state == "unavailable")
                return "The Ragnavik server is not ready to accept connections.\n\n" + context +
                    "It may be starting, restarting, or recovering from a failed update. Wait for the server to return; reinstalling mods will not fix server downtime.";
        }
        if (useServerStatus && context.Length == 0)
            context = "The server's required client pack version could not be verified. Check the server status announcement before changing your pack.\n\n";
        if (!string.IsNullOrWhiteSpace(catosRejection))
        {
            string detail = catosRejection.Trim();
            if (detail.StartsWith(CatosRejectionPrefix, StringComparison.OrdinalIgnoreCase))
                detail = detail[CatosRejectionPrefix.Length..].Trim();
            return "Your mod setup was rejected by the server.\n\n" + context +
                   "Use the exact client pack required by the server. If your pack is newer, wait for the server update or install the server-required version in Gale. " +
                   "If it is older, install the required pack. Do not update individual mods or CatosAntiCheat separately.\n\n" +
                   (detail.Length == 0 ? "The server did not provide a specific mod rejection reason." : "Server details:\n" + detail.Replace("<", "‹").Replace(">", "›"));
        }

        return context + ((NativeConnectionStatus)nativeStatus switch
        {
            NativeConnectionStatus.ErrorVersion => "Your Valheim game or network version does not match the server.\n\nCheck for a Valheim update. If your game just updated, the server may still be on the previous version; wait for the server update before retrying.",
            NativeConnectionStatus.ErrorConnectFailed => "The Ragnavik server could not be reached.\n\nThe server may be unavailable, restarting, or the network connection may have timed out. Try again in a moment.",
            NativeConnectionStatus.ErrorPassword => "The server password was not accepted.\n\nCheck the saved connection credentials and try again.",
            NativeConnectionStatus.ErrorBanned => "The server rejected this account.\n\nAccess may be blocked by the server. Contact a Ragnavik administrator if this is unexpected.",
            NativeConnectionStatus.ErrorFull => "The Ragnavik server is currently full.\n\nTry again when a player slot is available.",
            NativeConnectionStatus.ErrorPlatformExcluded => "The server does not allow this platform.\n\nUse a supported Valheim platform or contact a Ragnavik administrator.",
            NativeConnectionStatus.ErrorCrossplayPrivilege => "Your platform account does not currently allow crossplay.\n\nCheck the account's multiplayer and crossplay permissions, then try again.",
            NativeConnectionStatus.ErrorKicked => "The server rejected the connection.\n\nThis may be an access, authentication, or server policy rejection. Contact a Ragnavik administrator if it continues.",
            NativeConnectionStatus.ErrorDisconnected => "The connection ended before you could join.\n\nThe server may be restarting or the connection may have been interrupted. Try again in a moment.",
            NativeConnectionStatus.ErrorAlreadyConnected => "Valheim reports that this client is already connected.\n\nWait a moment for the previous session to close, then try again.",
            _ => "Valheim could not complete the connection for an unknown reason.\n\nTry again. If it continues, share the client log with a Ragnavik administrator."
        });
    }
}
