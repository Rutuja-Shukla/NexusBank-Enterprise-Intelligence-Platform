using NexusBank.Infrastructure.Security;

namespace NexusBank.Tests;

public sealed class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesUniqueSaltedValue_ThatVerifies()
    {
        var first = _hasher.Hash("NexusBank!2026");
        var second = _hasher.Hash("NexusBank!2026");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("NexusBank!2026", first);
        Assert.True(_hasher.Verify("NexusBank!2026", first));
        Assert.True(_hasher.Verify("NexusBank!2026", second));
    }

    [Fact]
    public void Verify_RejectsIncorrectPassword()
    {
        var hash = _hasher.Hash("correct-horse-battery");

        Assert.False(_hasher.Verify("Correct-horse-battery", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-bcrypt-hash")]
    public void Verify_ReturnsFalse_ForInvalidStoredHash(string stored)
    {
        Assert.False(_hasher.Verify("anything", stored));
    }
}
