using FluentAssertions;
using Microsoft.Extensions.Options;
using Qontract.Infrastructure.FileStorage;

namespace Qontract.Tests;

/// <summary>
/// Unit tests for the Azure Blob Storage backend (issue #25). No network:
/// a well-formed but fictional connection string is used (the SDK makes no
/// request at construction time), and only the offline parts of the
/// contract are verified — path layout, the max-size guard (which throws
/// before any SDK call), and fail-fast on missing configuration.
/// </summary>
public class AzureBlobFileStorageTests
{
    // Well-formed, fictional connection string — parsed but never used.
    // The Azure SDK validates the key at parse time (must be 512-bit /
    // 88 base64 chars), so use 64 zero bytes instead of an arbitrary
    // value. All sections are separated by ';' — a ',' would be swallowed
    // into the AccountKey value and fail to parse.
    private static readonly string FakeConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=" +
        Convert.ToBase64String(new byte[64]) +
        ";EndpointSuffix=core.windows.net";

    private static AzureBlobFileStorage NewStorage(int maxFileSizeBytes = 50 * 1024 * 1024)
    {
        return new AzureBlobFileStorage(
            Options.Create(new AzureBlobStorageOptions
            {
                ConnectionString = FakeConnectionString,
                ContainerName = "documents"
            }),
            Options.Create(new FileStorageOptions
            {
                MaxFileSizeBytes = maxFileSizeBytes
            }));
    }

    [Fact]
    public void GenerateFilePath_IsContainerRelative_WithForwardSlashes()
    {
        var storage = NewStorage();

        var path = storage.GenerateFilePath(7, "invoice.pdf", 3);

        path.Should().Be("contracts/7/3/invoice.pdf");
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
    public void Constructor_MissingConnectionString_ThrowsInvalidOperation()
    {
        var act = () => new AzureBlobFileStorage(
            Options.Create(new AzureBlobStorageOptions
            {
                ConnectionString = "  ",
                ContainerName = "documents"
            }),
            Options.Create(new FileStorageOptions()));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:Azure:ConnectionString*");
    }

    [Fact]
    public void Constructor_MissingContainerName_ThrowsInvalidOperation()
    {
        var act = () => new AzureBlobFileStorage(
            Options.Create(new AzureBlobStorageOptions
            {
                ConnectionString = FakeConnectionString,
                ContainerName = ""
            }),
            Options.Create(new FileStorageOptions()));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Storage:Azure:ContainerName*");
    }
}
