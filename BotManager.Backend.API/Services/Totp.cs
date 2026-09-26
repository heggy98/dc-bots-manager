using System.Security.Cryptography;
using System.Text;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Minimal RFC 6238 TOTP (HMAC-SHA1, 30 s steps) and RFC 4648 Base32 helpers.
    /// </summary>
    public static class Totp
    {
        /// <summary>Length of a time step in seconds.</summary>
        public const int StepSeconds = 30;

        /// <summary>Number of digits of generated codes.</summary>
        public const int DefaultDigits = 6;

        /// <summary>Accepted clock drift in time steps (before and after the current one).</summary>
        public const int AllowedDriftSteps = 1;

        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Returns the RFC 6238 time step (counter) for the given instant.
        /// </summary>
        public static long GetTimeStep(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

        /// <summary>
        /// Computes the HOTP value (RFC 4226) for a counter, zero-padded to <paramref name="digits"/>.
        /// </summary>
        public static string ComputeCode(byte[] key, long step, int digits = DefaultDigits)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (digits is < 6 or > 8)
                throw new ArgumentOutOfRangeException(nameof(digits));

            Span<byte> counter = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

            Span<byte> hash = stackalloc byte[20];
            HMACSHA1.HashData(key, counter, hash);

            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];

            var modulo = digits switch { 6 => 1_000_000, 7 => 10_000_000, _ => 100_000_000 };
            return (binary % modulo).ToString(new string('0', digits));
        }

        /// <summary>
        /// Verifies a code against the current step ±<see cref="AllowedDriftSteps"/>.
        /// Only steps strictly greater than <paramref name="lastUsedStep"/> are accepted (replay protection).
        /// All candidate steps are compared in constant time.
        /// </summary>
        /// <returns>The matched time step, or null when the code is invalid or replayed.</returns>
        public static long? Verify(byte[] key, string? code, DateTimeOffset now, long lastUsedStep = -1,
            int digits = DefaultDigits)
        {
            var normalized = NormalizeCode(code);
            if (normalized == null || normalized.Length != digits)
                return null;

            var provided = Encoding.ASCII.GetBytes(normalized);
            var current = GetTimeStep(now);
            long? matched = null;
            for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
            {
                var expected = Encoding.ASCII.GetBytes(ComputeCode(key, step, digits));
                // Evaluate every candidate so timing does not reveal which step matched.
                if (CryptographicOperations.FixedTimeEquals(expected, provided) && step > lastUsedStep && matched == null)
                {
                    matched = step;
                }
            }

            return matched;
        }

        /// <summary>
        /// Builds an otpauth:// URI understood by authenticator apps.
        /// </summary>
        public static string BuildOtpAuthUri(string issuer, string account, string base32Secret)
        {
            var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);
            return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}" +
                   $"&algorithm=SHA1&digits={DefaultDigits}&period={StepSeconds}";
        }

        /// <summary>
        /// Strips whitespace and dashes from a user-entered numeric code; returns null if it is not numeric.
        /// </summary>
        private static string? NormalizeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var builder = new StringBuilder(code.Length);
            foreach (var ch in code)
            {
                if (char.IsWhiteSpace(ch) || ch == '-')
                    continue;
                if (ch is < '0' or > '9')
                    return null;
                builder.Append(ch);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Encodes bytes as unpadded RFC 4648 Base32.
        /// </summary>
        public static string Base32Encode(ReadOnlySpan<byte> data)
        {
            var builder = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                    bits -= 5;
                }
            }

            if (bits > 0)
            {
                builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Decodes RFC 4648 Base32 (case-insensitive, padding and spaces ignored).
        /// </summary>
        public static byte[] Base32Decode(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var output = new List<byte>(value.Length * 5 / 8);
            int buffer = 0, bits = 0;
            foreach (var raw in value)
            {
                if (raw == '=' || char.IsWhiteSpace(raw))
                    continue;

                var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(raw));
                if (index < 0)
                    throw new FormatException("Invalid Base32 character.");

                buffer = (buffer << 5) | index;
                bits += 5;
                if (bits >= 8)
                {
                    output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }

            return output.ToArray();
        }
    }
}
