namespace BotManager.Backend.API.Services
{
    public interface IEmojiCatalogService
    {
        Task<IReadOnlyList<string>> GetEmojiCatalogAsync(CancellationToken cancellationToken = default);
    }
}
