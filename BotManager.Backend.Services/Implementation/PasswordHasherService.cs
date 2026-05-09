using BotManager.Backend.Services.Interfaces;

namespace BotManager.Backend.Services.Implementation
{
    public class PasswordHasherService : IPasswordHasherService
    {
        public string HashPassword(string password)
        {
            if(password == null)
                throw new ArgumentNullException(nameof(password));

            if(password.Length == 0)
                throw new ArgumentException("Password cannot be empty", nameof(password));

            var res = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
            Console.WriteLine($"Vygenerovaný Hash: {res}");
            return res;
        }

        // Ověří zadané heslo proti uloženému hashi
        public bool VerifyPassword(string password, string hashedPassword)
        {
            if(password == null)
                throw new ArgumentNullException(nameof(password));
            if(hashedPassword == null)
                throw new ArgumentException(nameof(hashedPassword));

            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
