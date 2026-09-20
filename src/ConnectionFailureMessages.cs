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

    internal static string Format(int nativeStatus, string? catosRejection)
    {
        if (!string.IsNullOrWhiteSpace(catosRejection))
        {
            string detail = catosRejection.Trim();
            if (detail.StartsWith(CatosRejectionPrefix, StringComparison.OrdinalIgnoreCase))
                detail = detail[CatosRejectionPrefix.Length..].Trim();
            return "Your mod setup was rejected by the server.\n\n" +
                   (detail.Length == 0 ? "Update the Ragnavik client pack and CatosAntiCheat, then try again." : detail) +
                   "\n\nUpdate the Ragnavik client pack and CatosAntiCheat before retrying.";
        }

        return (NativeConnectionStatus)nativeStatus switch
        {
            NativeConnectionStatus.ErrorVersion => "Your Valheim game or network version does not match the server.\n\nUpdate Valheim, then restart Gale and try again.",
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
        };
    }
}
