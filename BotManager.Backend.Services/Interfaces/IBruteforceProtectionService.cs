namespace BotManager.Backend.Services.Interfaces
{
    public interface IBruteforceProtectionService
    {
        bool IsLocked(string ipAddress);
        void RegisterFailure(string ipAddress);
        void RegisterSuccess(string ipAddress);
        int GetFailedAttempts(string ipAddress);
    }
}
