using FluentAssertions;
using Qontract.Infrastructure.FileStorage;

namespace Qontract.Tests;

/// <summary>
/// Tests for the storage backend selection (issue #25): normalization of
/// the "Storage:Provider" value, including the fail-fast on unknown values
/// (a typo must be a startup error, not a silent fallback to disk).
/// </summary>
public class StorageProviderTests
{
    [Theory]
    [InlineData(null, "local")]
    [InlineData("", "local")]
    [InlineData("   ", "local")]
    [InlineData("local", "local")]
    [InlineData("Local", "local")]
    [InlineData(" LOCAL ", "local")]
    [InlineData("azure", "azure")]
    [InlineData("Azure", "azure")]
    [InlineData("s3", "s3")]
    [InlineData("S3", "s3")]
    [InlineData(" s3 ", "s3")]
    public void Normalize_Trims_IsCaseInsensitive_AndDefaultsToLocal(string? raw, string expected)
    {
        StorageProvider.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("gcs")]
    [InlineData("Azure Blob")]
    [InlineData("LOCAL2")]
    public void Normalize_UnknownValue_ThrowsInvalidOperation(string raw)
    {
        var act = () => StorageProvider.Normalize(raw);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Unknown Storage:Provider '{raw}'*");
    }

    [Fact]
    public void FileStorageOptions_DefaultProvider_IsLocal()
    {
        new FileStorageOptions().Provider.Should().Be(StorageProvider.Local);
    }
}
