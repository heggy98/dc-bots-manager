namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Provides secure token storage and display helpers.
    /// </summary>
    public interface IBotTokenSecurityService
    {
        /// <summary>
        /// Validates and normalizes raw token input.
        /// </summary>
        string NormalizeRawToken(string rawToken);

        /// <summary>
        /// Protects a raw token into storage-safe envelope that contains hash + protected payload.
        /// </summary>
        string ProtectForStorage(string rawToken);

        /// <summary>
        /// Tries to restore a raw token from database value. Supports legacy plain-text rows.
        /// </summary>
        bool TryGetRawToken(string storedToken, out string rawToken);

        /// <summary>
        /// Returns token masked for UI display (first 3 chars + stars).
        /// </summary>
        string BuildMaskedToken(string storedToken);
    }
}
