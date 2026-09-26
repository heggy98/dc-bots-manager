using BotManager.Backend.API.Services;
using BotManager.Backend.Services.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class AuthSecurityTests
{
    private const string ValidSecret = "0123456789abcdef0123456789abcdef-test";

    [Fact]
    public void AdminCredentials_MissingConfiguration_FailsAtConstruction()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(() =>
            new AdminCredentialsService(config, new FakeHostEnvironment("Production"), new PasswordHasherService()));
    }

    [Fact]
    public void AdminCredentials_PlainPasswordOutsideDevelopment_IsRejected()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AdminCredentials:Email"] = "admin@example.com",
            ["AdminCredentials:Password"] = "secret"
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AdminCredentialsService(config, new FakeHostEnvironment("Production"), new PasswordHasherService()));
    }

    [Fact]
    public void AdminCredentials_VerifiesBcryptHash_CaseInsensitiveEmail()
    {
        var hasher = new PasswordHasherService();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AdminCredentials:Email"] = "Admin@Example.com",
            ["AdminCredentials:PasswordHash"] = hasher.HashPassword("correct horse")
        });
        var sut = new AdminCredentialsService(config, new FakeHostEnvironment("Production"), hasher);

        Assert.True(sut.Verify("admin@example.com", "correct horse"));
        Assert.False(sut.Verify("admin@example.com", "wrong"));
        Assert.False(sut.Verify("other@example.com", "correct horse"));
        Assert.False(sut.Verify(null, null));
        Assert.False(sut.Verify("admin@example.com", ""));
    }

    [Fact]
    public void AdminCredentials_PlainPasswordInDevelopment_Works()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AdminCredentials:Email"] = "admin@example.com",
            ["AdminCredentials:Password"] = "dev-password"
        });
        var sut = new AdminCredentialsService(config, new FakeHostEnvironment("Development"), new PasswordHasherService());

        Assert.True(sut.Verify("admin@example.com", "dev-password"));
        Assert.False(sut.Verify("admin@example.com", "dev-passwor"));
    }

    [Fact]
    public void JwtTokenService_ShortSecret_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["JwtSettings:Secret"] = "too-short" });

        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(config));
    }

    [Fact]
    public void JwtTokenService_IssuesAdminTokenWithUtcExpiry()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = ValidSecret,
            ["JwtSettings:Issuer"] = "BotManager",
            ["JwtSettings:Audience"] = "BotManager",
            ["JwtSettings:ExpiryMinutes"] = "60"
        });
        var sut = new JwtTokenService(config);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(sut.GenerateToken("admin@example.com"));

        Assert.Contains(token.Claims, c => c.Value == AuthRoles.Admin);
        Assert.InRange(token.ValidTo, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void Bruteforce_LocksAfterConfiguredAttempts_AndUnlocksAfterLockout()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var sut = new BruteforceProtectionService(() => now);

        sut.RegisterFailure("1.2.3.4", maxAttempts: 3, lockoutMinutes: 10);
        sut.RegisterFailure("1.2.3.4", maxAttempts: 3, lockoutMinutes: 10);
        Assert.False(sut.IsLocked("1.2.3.4"));

        sut.RegisterFailure("1.2.3.4", maxAttempts: 3, lockoutMinutes: 10);
        Assert.True(sut.IsLocked("1.2.3.4"));

        now = now.AddMinutes(11);
        Assert.False(sut.IsLocked("1.2.3.4"));
        Assert.Equal(0, sut.GetFailedAttempts("1.2.3.4"));
    }

    [Fact]
    public void Bruteforce_StaleEntriesAreForgottenAndSwept()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var sut = new BruteforceProtectionService(() => now);

        for (var i = 0; i < 100; i++)
        {
            sut.RegisterFailure($"10.0.0.{i}", maxAttempts: 5, lockoutMinutes: 5);
        }

        Assert.Equal(100, sut.TrackedKeyCount);

        now = now.AddMinutes(20);
        Assert.Equal(0, sut.GetFailedAttempts("10.0.0.1"));

        sut.RegisterFailure("192.168.0.1", maxAttempts: 5, lockoutMinutes: 5);
        Assert.Equal(1, sut.TrackedKeyCount);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
