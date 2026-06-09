using BotManager.Backend.Bots.Services.Implementations;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class BotTokenSecurityServiceTests
{
    [Fact]
    public void NormalizeRawToken_EmptyInput_ReturnsEmptyString()
    {
        var sut = CreateSut();

        var normalized = sut.NormalizeRawToken("   ");

        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void NormalizeRawToken_TrimsQuotesAndBotPrefix()
    {
        var sut = CreateSut();

        var normalized = sut.NormalizeRawToken("  \"Bot abc.def.ghi\"  ");

        Assert.Equal("abc.def.ghi", normalized);
    }

    [Fact]
    public void ProtectAndRestore_RoundTrip_ReturnsOriginalNormalizedToken()
    {
        var sut = CreateSut();
        const string rawToken = "Bot super-secret-token";

        var stored = sut.ProtectForStorage(rawToken);
        var ok = sut.TryGetRawToken(stored, out var restored);

        Assert.True(ok);
        Assert.Equal("super-secret-token", restored);
        Assert.StartsWith("v1:", stored);
    }

    [Fact]
    public void ProtectForStorage_EmptyInput_ReturnsEmptyString()
    {
        var sut = CreateSut();

        var stored = sut.ProtectForStorage("   ");

        Assert.Equal(string.Empty, stored);
    }

    [Fact]
    public void TryGetRawToken_LegacyPlainTextToken_Succeeds()
    {
        var sut = CreateSut();

        var ok = sut.TryGetRawToken("  Bot old-legacy-token  ", out var restored);

        Assert.True(ok);
        Assert.Equal("old-legacy-token", restored);
    }

    [Fact]
    public void TryGetRawToken_InvalidEnvelope_ReturnsFalse()
    {
        var sut = CreateSut();

        var ok = sut.TryGetRawToken("v1:ABC123:not-protected-payload", out var restored);

        Assert.False(ok);
        Assert.Equal(string.Empty, restored);
    }

    [Fact]
    public void BuildMaskedToken_UsesFirstThreeCharactersAndStars()
    {
        var sut = CreateSut();
        var stored = sut.ProtectForStorage("abcdefghi");

        var masked = sut.BuildMaskedToken(stored);

        Assert.StartsWith("abc", masked);
        Assert.DoesNotContain("defghi", masked);
        Assert.True(masked.Length >= 9);
        Assert.True(masked.Skip(3).All(ch => ch == '*'));
    }

    [Fact]
    public void BuildMaskedToken_InvalidStoredValue_ReturnsFallbackMask()
    {
        var sut = CreateSut();

        var masked = sut.BuildMaskedToken("v1:invalid:nope");

        Assert.Equal("***", masked);
    }

    [Fact]
    public void BuildMaskedToken_ShortToken_StillUsesStarMask()
    {
        var sut = CreateSut();
        var stored = sut.ProtectForStorage("ab");

        var masked = sut.BuildMaskedToken(stored);

        Assert.StartsWith("ab", masked);
        Assert.Equal(8, masked.Length);
        Assert.True(masked.Skip(2).All(ch => ch == '*'));
    }

    private static BotTokenSecurityService CreateSut()
    {
        var provider = new FakeDataProtectionProvider();
        return new BotTokenSecurityService(provider);
    }

    private sealed class FakeDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return new FakeDataProtector();
        }
    }

    private sealed class FakeDataProtector : IDataProtector
    {
        private const string Prefix = "enc:";

        public IDataProtector CreateProtector(string purpose)
        {
            return this;
        }

        public byte[] Protect(byte[] plaintext)
        {
            var encoded = Prefix + Convert.ToBase64String(plaintext);
            return System.Text.Encoding.UTF8.GetBytes(encoded);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var value = System.Text.Encoding.UTF8.GetString(protectedData);
            if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected token format.");
            }

            return Convert.FromBase64String(value[Prefix.Length..]);
        }
    }
}
