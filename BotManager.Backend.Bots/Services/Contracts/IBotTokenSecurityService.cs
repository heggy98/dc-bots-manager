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
        /// Tries to restore a raw token from database value. Plain-text (legacy) values are rejected.
        /// </summary>
        bool TryGetRawToken(string storedToken, out string rawToken);

        /// <summary>
        /// Returns whether a stored value is in the protected envelope format.
        /// </summary>
        bool IsProtected(string storedToken);

        /// <summary>
        /// Converts a legacy plain-text stored token into the protected envelope format.
        /// Returns false when the value is empty or already protected.
        /// </summary>
        bool TryProtectLegacyToken(string storedToken, out string protectedToken);

        /// <summary>
        /// Returns token masked for UI display (first 3 chars + stars).
        /// </summary>
        string BuildMaskedToken(string storedToken);
    }
}
