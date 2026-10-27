using AuthServer.Shared;

namespace AuthServer.Sync.Domain;

/// <summary>Sync's expected failures; the code is the Problem Details type, public API.</summary>
internal static class SyncErrors
{
    public static readonly Error FileInvalid = new("sync.file-invalid", "That is not a store file (magic, format 3, kind, cipher, length).");

    public static readonly Error FilePlain = new("sync.file-plain", "A store is synced encrypted only: the server never holds a readable one.");

    public static readonly Error FileTooLarge = new("sync.file-too-large", "The store file is larger than the server accepts.");

    public static readonly Error NotNewer = new("sync.not-newer", "The server holds this version or a later one.");

    public static readonly Error StoreNotFound = new("sync.store-not-found", "There is no such store.");

    public static readonly Error RequestInvalid = new("sync.request-invalid", "A store is named by its store id (16 hex), seed id (8 hex) and kind (1 or 2).");
}
