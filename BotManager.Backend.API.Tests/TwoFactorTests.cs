using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class TotpTests
{
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 6238 Appendix B (SHA1).
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void ComputeCode_MatchesRfc6238Vectors(long unixSeconds, string expected)
    {
        var step = Totp.GetTimeStep(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.ComputeCode(RfcKey, step, digits: 8));
    }

    // RFC 4226 Appendix D (6 digits).
    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(5, "254676")]
    [InlineData(9, "520489")]
    public void ComputeCode_MatchesRfc4226Vectors(long counter, string expected)
    {
        Assert.Equal(expected, Totp.ComputeCode(RfcKey, counter));
    }

    [Fact]
    public void Verify_AcceptsOneStepDrift_RejectsTwo()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.GetTimeStep(now);

        Assert.Equal(step - 1, Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step - 1), now));
        Assert.Equal(step + 1, Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step + 1), now));
        Assert.Null(Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step - 2), now));
        Assert.Null(Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step + 2), now));
    }

    [Fact]
    public void Verify_RejectsReplayOfLastUsedStep()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.GetTimeStep(now);
        var code = Totp.ComputeCode(RfcKey, step);

        Assert.Equal(step, Totp.Verify(RfcKey, code, now, lastUsedStep: step - 1));
        Assert.Null(Totp.Verify(RfcKey, code, now, lastUsedStep: step));
        // Older (but still in window) codes are rejected once a newer step was used.
        Assert.Null(Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step - 1), now, lastUsedStep: step));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Verify_RejectsMalformedCodes(string? code)
    {
        Assert.Null(Totp.Verify(RfcKey, code, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_IgnoresSpacesAndDashes()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = Totp.ComputeCode(RfcKey, Totp.GetTimeStep(now));

        Assert.NotNull(Totp.Verify(RfcKey, code[..3] + " " + code[3..], now));
    }

    [Fact]
    public void Base32_RoundTripsAndMatchesRfc4648()
    {
        Assert.Equal("MZXW6YTBOI", Totp.Base32Encode(Encoding.ASCII.GetBytes("foobar")));
        Assert.Equal("foobar", Encoding.ASCII.GetString(Totp.Base32Decode("mzxw6ytboi======")));

        var random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        Assert.Equal(random, Totp.Base32Decode(Totp.Base32Encode(random)));
    }

    [Fact]
    public void OtpAuthUri_ContainsSecretAndIssuer()
    {
        var uri = Totp.BuildOtpAuthUri("Bot Manager", "admin@example.com", "ABCDEF");

        Assert.StartsWith("otpauth://totp/Bot%20Manager:admin%40example.com?secret=ABCDEF", uri);
        Assert.Contains("issuer=Bot%20Manager", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}

public class TwoFactorServiceTests
{
    private readonly DbContextOptions<BotManagerDbContext> _options = new DbContextOptionsBuilder<BotManagerDbContext>()
        .UseInMemoryDatabase("twofactor-" + Guid.NewGuid())
        .Options;

    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly FakeTime _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    private TwoFactorService CreateService(out BotManagerDbContext db)
    {
        db = new BotManagerDbContext(_options);
        return new TwoFactorService(db, _protection, new ConfigurationBuilder().Build(), _time);
    }

    private async Task<(string Secret, IReadOnlyList<string> RecoveryCodes)> EnableAsync()
    {
        var service = CreateService(out var db);
        await using var _ = db;
        var setup = await service.BeginSetupAsync("admin@example.com");
        Assert.NotNull(setup);
        var codes = await service.EnableAsync(CurrentCode(setup!.Secret));
        Assert.NotNull(codes);
        return (setup.Secret, codes!);
    }

    private string CurrentCode(string secret, int stepOffset = 0)
        => Totp.ComputeCode(Totp.Base32Decode(secret), Totp.GetTimeStep(_time.GetUtcNow()) + stepOffset);

    [Fact]
    public async Task Setup_DoesNotEnableUntilConfirmed()
    {
        var service = CreateService(out var db);
        var setup = await service.BeginSetupAsync("admin@example.com");

        Assert.NotNull(setup);
        Assert.Contains("secret=" + setup!.Secret, setup.OtpAuthUri);
        Assert.False(await service.IsEnabledAsync());
        Assert.Null(await service.EnableAsync(CurrentCode(setup.Secret, stepOffset: 5)));
        Assert.False(await service.IsEnabledAsync());

        // The secret is stored encrypted, never in plain text.
        Assert.DoesNotContain(await db.SystemConfigs.Select(c => c.Value).ToListAsync(), v => v.Contains(setup.Secret));
    }

    [Fact]
    public async Task Enable_ReturnsEightRecoveryCodes_StoredOnlyAsHashes()
    {
        var (secret, codes) = await EnableAsync();

        Assert.Equal(TwoFactorService.RecoveryCodeCount, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", c));

        await using var db = new BotManagerDbContext(_options);
        var values = await db.SystemConfigs.Select(c => c.Value).ToListAsync();
        Assert.DoesNotContain(values, v => codes.Any(code => v.Contains(code)) || v.Contains(secret));
        var status = await CreateService(out _).GetStatusAsync();
        Assert.True(status.Enabled);
        Assert.Equal(8, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task Verify_RejectsReuseOfTheSameTimeStep()
    {
        var (secret, _) = await EnableAsync();
        // The enable code consumed the current step; move on one step.
        _time.Advance(TimeSpan.FromSeconds(30));
        var code = CurrentCode(secret);

        Assert.Equal(TwoFactorCheckResult.ValidTotp, await CreateService(out _).VerifyAsync(code, null));
        Assert.Equal(TwoFactorCheckResult.Invalid, await CreateService(out _).VerifyAsync(code, null));
    }

    [Fact]
    public async Task Verify_RejectsTheEnableCodeReplayedAtLogin()
    {
        var (secret, _) = await EnableAsync();

        Assert.Equal(TwoFactorCheckResult.Invalid, await CreateService(out _).VerifyAsync(CurrentCode(secret), null));
    }

    [Fact]
    public async Task RecoveryCodes_AreSingleUse_AndCaseInsensitive()
    {
        var (_, codes) = await EnableAsync();

        Assert.Equal(TwoFactorCheckResult.ValidRecoveryCode,
            await CreateService(out _).VerifyAsync(null, codes[3].ToLowerInvariant().Replace("-", " ")));
        Assert.Equal(TwoFactorCheckResult.Invalid, await CreateService(out _).VerifyAsync(null, codes[3]));
        Assert.Equal(TwoFactorCheckResult.Invalid, await CreateService(out _).VerifyAsync(null, "AAAAA-AAAAA"));
        Assert.Equal(7, (await CreateService(out _).GetStatusAsync()).RecoveryCodesRemaining);
    }

    [Fact]
    public async Task Disable_RemovesAllTwoFactorState()
    {
        await EnableAsync();

        await CreateService(out _).DisableAsync();

        await using var db = new BotManagerDbContext(_options);
        Assert.False(await db.SystemConfigs.AnyAsync(c => c.Key.StartsWith(TwoFactorService.KeyPrefix)));
        Assert.False(await CreateService(out _).IsEnabledAsync());
    }

    [Fact]
    public async Task Setup_WhenAlreadyEnabled_ReturnsNull()
    {
        await EnableAsync();

        Assert.Null(await CreateService(out _).BeginSetupAsync("admin@example.com"));
    }

    [Fact]
    public void RecoveryCodeHash_IsNormalized()
    {
        Assert.Equal(TwoFactorService.HashRecoveryCode("ABCDE-FGHJK"), TwoFactorService.HashRecoveryCode(" abcde fghjk "));
        Assert.Equal(64, TwoFactorService.HashRecoveryCode("ABCDE-FGHJK").Length);
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
