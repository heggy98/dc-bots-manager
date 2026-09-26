using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BotManager.Backend.API.Services
{
    /// <summary>Current two-factor state of the admin account.</summary>
    public sealed record TwoFactorStatus(bool Enabled, bool PendingSetup, int RecoveryCodesRemaining);

    /// <summary>Data shown to the admin while setting up an authenticator app.</summary>
    public sealed record TwoFactorSetup(string Secret, string OtpAuthUri);

    /// <summary>Result of a second-factor check.</summary>
    public enum TwoFactorCheckResult
    {
        /// <summary>No valid code was supplied.</summary>
        Invalid = 0,
        /// <summary>A valid TOTP code was supplied (its time step is now consumed).</summary>
        ValidTotp = 1,
        /// <summary>A valid recovery code was supplied (it is now consumed).</summary>
        ValidRecoveryCode = 2
    }

    /// <summary>
    /// TOTP two-factor authentication for the single admin account. The secret is stored in SystemConfigs
    /// encrypted with ASP.NET Data Protection; recovery codes are stored as SHA-256 hashes only.
    /// </summary>
    public class TwoFactorService
    {
        /// <summary>Prefix shared by every 2FA SystemConfig key (hidden from the generic config API).</summary>
        public const string KeyPrefix = "Auth.Totp";
        /// <summary>"true" when 2FA is enforced.</summary>
        public const string EnabledKey = "Auth.TotpEnabled";
        /// <summary>Protected active secret.</summary>
        public const string SecretKey = "Auth.TotpSecret";
        /// <summary>Protected secret of a setup that has not been confirmed yet.</summary>
        public const string PendingSecretKey = "Auth.TotpPendingSecret";
        /// <summary>Last accepted time step (replay protection).</summary>
        public const string LastStepKey = "Auth.TotpLastStep";
        /// <summary>Comma separated SHA-256 hashes of unused recovery codes.</summary>
        public const string RecoveryCodesKey = "Auth.TotpRecoveryCodes";

        /// <summary>Number of recovery codes generated on enable.</summary>
        public const int RecoveryCodeCount = 8;

        private const int SecretBytes = 20;
        private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        // Serializes verify-and-consume so one code/time step cannot be used twice concurrently.
        private static readonly SemaphoreSlim ConsumeLock = new(1, 1);

        private readonly BotManagerDbContext _db;
        private readonly IDataProtector _protector;
        private readonly TimeProvider _time;
        private readonly string _issuer;

        /// <summary>
        /// Creates a new two-factor service.
        /// </summary>
        public TwoFactorService(BotManagerDbContext db, IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration, TimeProvider? timeProvider = null)
        {
            _db = db;
            _protector = dataProtectionProvider.CreateProtector("BotManager.Auth.Totp.v1");
            _time = timeProvider ?? TimeProvider.System;
            var issuer = configuration["Auth:TotpIssuer"];
            _issuer = string.IsNullOrWhiteSpace(issuer) ? "BotManager" : issuer.Trim();
        }

        /// <summary>
        /// Returns whether 2FA is currently enforced.
        /// </summary>
        public async Task<bool> IsEnabledAsync()
            => string.Equals(await GetValueAsync(EnabledKey), "true", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrEmpty(await GetValueAsync(SecretKey));

        /// <summary>
        /// Returns the current 2FA state.
        /// </summary>
        public async Task<TwoFactorStatus> GetStatusAsync()
        {
            var enabled = await IsEnabledAsync();
            var pending = !string.IsNullOrEmpty(await GetValueAsync(PendingSecretKey));
            var remaining = enabled ? ParseHashes(await GetValueAsync(RecoveryCodesKey)).Count : 0;
            return new TwoFactorStatus(enabled, pending, remaining);
        }

        /// <summary>
        /// Generates a new pending secret (2FA is not enforced until <see cref="EnableAsync"/> succeeds).
        /// Returns null when 2FA is already enabled.
        /// </summary>
        public async Task<TwoFactorSetup?> BeginSetupAsync(string accountName)
        {
            if (await IsEnabledAsync())
                return null;

            var secret = Totp.Base32Encode(RandomNumberGenerator.GetBytes(SecretBytes));
            await SetValueAsync(PendingSecretKey, _protector.Protect(secret));
            await _db.SaveChangesAsync();

            return new TwoFactorSetup(secret, Totp.BuildOtpAuthUri(_issuer, accountName, secret));
        }

        /// <summary>
        /// Confirms the pending setup with a code from the authenticator app, enables 2FA and returns
        /// freshly generated recovery codes (shown once). Returns null when the code is invalid.
        /// </summary>
        public async Task<IReadOnlyList<string>?> EnableAsync(string? code)
        {
            await ConsumeLock.WaitAsync();
            try
            {
                if (await IsEnabledAsync())
                    return null;

                var protectedPending = await GetValueAsync(PendingSecretKey);
                var key = UnprotectKey(protectedPending);
                if (key == null)
                    return null;

                var step = Totp.Verify(key, code, _time.GetUtcNow());
                if (step == null)
                    return null;

                var codes = GenerateRecoveryCodes();
                await SetValueAsync(SecretKey, protectedPending!);
                await SetValueAsync(PendingSecretKey, null);
                await SetValueAsync(LastStepKey, step.Value.ToString(CultureInfo.InvariantCulture));
                await SetValueAsync(RecoveryCodesKey, string.Join(',', codes.Select(HashRecoveryCode)));
                await SetValueAsync(EnabledKey, "true");
                await _db.SaveChangesAsync();
                return codes;
            }
            finally
            {
                ConsumeLock.Release();
            }
        }

        /// <summary>
        /// Checks a TOTP code (or, if given, a recovery code) against the active secret and consumes it.
        /// </summary>
        public async Task<TwoFactorCheckResult> VerifyAsync(string? totpCode, string? recoveryCode)
        {
            await ConsumeLock.WaitAsync();
            try
            {
                if (!await IsEnabledAsync())
                    return TwoFactorCheckResult.Invalid;

                if (!string.IsNullOrWhiteSpace(recoveryCode))
                {
                    return await ConsumeRecoveryCodeAsync(recoveryCode)
                        ? TwoFactorCheckResult.ValidRecoveryCode
                        : TwoFactorCheckResult.Invalid;
                }

                var key = UnprotectKey(await GetValueAsync(SecretKey));
                if (key == null)
                    return TwoFactorCheckResult.Invalid;

                var lastStep = long.TryParse(await GetValueAsync(LastStepKey), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
                var step = Totp.Verify(key, totpCode, _time.GetUtcNow(), lastStep);
                if (step == null)
                    return TwoFactorCheckResult.Invalid;

                await SetValueAsync(LastStepKey, step.Value.ToString(CultureInfo.InvariantCulture));
                await _db.SaveChangesAsync();
                return TwoFactorCheckResult.ValidTotp;
            }
            finally
            {
                ConsumeLock.Release();
            }
        }

        /// <summary>
        /// Turns 2FA off and removes the secret and recovery codes.
        /// </summary>
        public async Task DisableAsync()
        {
            var rows = await _db.SystemConfigs.Where(c => c.Key.StartsWith(KeyPrefix)).ToListAsync();
            _db.SystemConfigs.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Normalizes and hashes a recovery code for storage/comparison.
        /// </summary>
        public static string HashRecoveryCode(string code)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(code))));

        /// <summary>
        /// Generates <see cref="RecoveryCodeCount"/> random codes formatted as XXXXX-XXXXX (50 bits each).
        /// </summary>
        public static IReadOnlyList<string> GenerateRecoveryCodes()
        {
            var codes = new List<string>(RecoveryCodeCount);
            while (codes.Count < RecoveryCodeCount)
            {
                var chars = new char[10];
                for (var i = 0; i < chars.Length; i++)
                {
                    chars[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
                }

                var code = new string(chars, 0, 5) + "-" + new string(chars, 5, 5);
                if (!codes.Contains(code))
                    codes.Add(code);
            }

            return codes;
        }

        private async Task<bool> ConsumeRecoveryCodeAsync(string recoveryCode)
        {
            var hashes = ParseHashes(await GetValueAsync(RecoveryCodesKey));
            var provided = Encoding.ASCII.GetBytes(HashRecoveryCode(recoveryCode));
            var matchIndex = -1;
            for (var i = 0; i < hashes.Count; i++)
            {
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hashes[i]), provided) && matchIndex < 0)
                    matchIndex = i;
            }

            if (matchIndex < 0)
                return false;

            hashes.RemoveAt(matchIndex);
            await SetValueAsync(RecoveryCodesKey, string.Join(',', hashes));
            await _db.SaveChangesAsync();
            return true;
        }

        private static string NormalizeRecoveryCode(string code)
            => new(code.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());

        private static List<string> ParseHashes(string? value)
            => string.IsNullOrEmpty(value)
                ? new List<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        private byte[]? UnprotectKey(string? protectedSecret)
        {
            if (string.IsNullOrEmpty(protectedSecret))
                return null;

            try
            {
                return Totp.Base32Decode(_protector.Unprotect(protectedSecret));
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private async Task<string?> GetValueAsync(string key)
        {
            var row = _db.SystemConfigs.Local.FirstOrDefault(c => c.Key == key)
                      ?? await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            return row?.Value;
        }

        /// <summary>Tracks a value change (null removes the row); caller saves.</summary>
        private async Task SetValueAsync(string key, string? value)
        {
            var row = _db.SystemConfigs.Local.FirstOrDefault(c => c.Key == key)
                      ?? await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (value == null)
            {
                if (row != null)
                    _db.SystemConfigs.Remove(row);
                return;
            }

            if (row == null)
            {
                _db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
            }
            else
            {
                row.Value = value;
            }
        }
    }
}
