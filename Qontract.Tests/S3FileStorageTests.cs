using FluentAssertions;
using Microsoft.Extensions.Options;
using Qontract.Infrastructure.FileStorage;

namespace Qontract.Tests;

/// <summary>
/// Unit tests for the Amazon S3 backend (issue #25). No network: the
/// client is constructed against a real region name (offline), and only
/// the offline parts of the contract are verified — key layout, prefix
/// mapping, the max-size guard (which throws before any SDK call), and
/// fail-fast on missing/invalid configuration.
/// </summary>
public class S3FileStorageTests
{
    private static S3FileStorage NewStorage(
        string bucket = "test-bucket",
        string prefix = "",
        int maxFileSizeBytes = 50 * 1024 * 1024)
    {
        return new S3FileStorage(
            Options.Create(new S3StorageOptions
            {
                Bucket = bucket,
                Prefix = prefix,
                Region = "us-east-1"
            }),
            Options.Create(new FileStorageOptions
            {
                MaxFileSizeBytes = maxFileSizeBytes
            }));
    }

    [Fact]
    public void GenerateFilePath_IsKeyRelative_WithForwardSlashes()
    {
        var storage = NewStorage();

        storage.GenerateFilePath(7, "invoice.pdf", 3)
            .Should().Be("contracts/7/3/invoice.pdf");
    }

    [Theory]
    [InlineData("qontract")]
    [InlineData("/qontract/")]
    [InlineData("  qontract  ")]
    public void ToObjectKey_AppliesNormalizedPrefix(string prefix)
    {
        var storage = NewStorage(prefix: prefix);

        storage.ToObjectKey("contracts/7/3/invoice.pdf")
            .Should().Be("qontract/contracts/7/3/invoice.pdf");
    }

    [Fact]
    public void ToObjectKey_WithoutPrefix_ReturnsKeyUnchanged()
    {
        var storage = NewStorage(prefix: "");

        storage.ToObjectKey("contracts/7/3/invoice.pdf")
            .Should().Be("contracts/7/3/invoice.pdf");
    }

    [Fact]
    public async Task SaveAsync_OverMaxSize_ThrowsInvalidOperation_BeforeAnyNetworkCall()
    {
        var storage = NewStorage(maxFileSizeBytes: 10);

        var act = () => storage.SaveAsync(new byte[11], "contracts/1/1/too-big.bin");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds maximum allowed size*");
    }

    [Fact]
    public void Constructor_MissingBucket_ThrowsInvalidOperation()
    {
        var act = () => new S3FileStorage(
            Options.Create(new S3StorageOptions
            {
                Bucket = " ",
                Region = "us-east-1"
            }),
            Options.Create(new FileStorageOptions()));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:S3:Bucket*");
    }

    [Fact]
    public void Constructor_WithExplicitRegion_ConstructsOffline()
    {
        // GetBySystemName accepts real and virtual region names alike, so
        // construction must succeed without a network round trip.
        var act = () => new S3FileStorage(
            Options.Create(new S3StorageOptions
            {
                Bucket = "test-bucket",
                Region = "eu-central-1"
            }),
            Options.Create(new FileStorageOptions()));

        act.Should().NotThrow();
    }
}
