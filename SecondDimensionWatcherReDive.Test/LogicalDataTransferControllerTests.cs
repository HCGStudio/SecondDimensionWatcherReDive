using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class LogicalDataTransferControllerTests
{
    [TestMethod]
    public async Task ExportSelectsCategoriesAndReturnsChecksummedEnvelope()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        repository.Setup(item => item.ExportAsync(
                LogicalDataCategory.Feeds | LogicalDataCategory.Playback,
                Guid.Empty,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogicalDataCategory categories, Guid _, string version, CancellationToken _) =>
                Bundle(version, categories));
        var controller = Controller(repository.Object);

        var action = await controller.ExportAsync("feeds,playback", CancellationToken.None);

        var file = (FileContentResult)action;
        Assert.AreEqual("application/json", file.ContentType);
        StringAssert.StartsWith(file.FileDownloadName!, "sdw-logical-export-");
        var envelope = JsonSerializer.Deserialize(
            file.FileContents,
            AppJsonSerializerContext.Default.LogicalDataExportEnvelope)!;
        Assert.AreEqual(LogicalDataCategory.Feeds | LogicalDataCategory.Playback,
            envelope.Data.Categories);
        Assert.AreEqual(Digest(envelope.Data), envelope.Sha256);
    }

    [TestMethod]
    public async Task ImportRejectsChecksumMismatchBeforeRepositoryWrite()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        var controller = Controller(repository.Object);
        var request = new LogicalDataImportRequest(
            Bundle("1.0.0", LogicalDataCategory.Feeds),
            new string('0', 64),
            LogicalImportConflictStrategy.Skip);

        var action = await controller.ImportAsync(request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(action);
        repository.Verify(item => item.ImportAsync(
            It.IsAny<LogicalDataBundle>(),
            It.IsAny<LogicalImportConflictStrategy>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportRejectsUnknownConflictStrategyBeforeRepositoryWrite()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        var controller = Controller(repository.Object);
        var bundle = Bundle("1.0.0", LogicalDataCategory.Feeds);
        var request = new LogicalDataImportRequest(
            bundle,
            Digest(bundle),
            (LogicalImportConflictStrategy)999);

        var action = await controller.ImportAsync(request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(action);
        repository.Verify(item => item.ImportAsync(
            It.IsAny<LogicalDataBundle>(),
            It.IsAny<LogicalImportConflictStrategy>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportPassesValidatedBundleAndConflictStrategy()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        repository.Setup(item => item.ExportAsync(
                It.IsAny<LogicalDataCategory>(), Guid.Empty, It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogicalDataCategory categories, Guid _, string version, CancellationToken _) =>
                Bundle(version, categories));
        repository.Setup(item => item.ImportAsync(
                It.IsAny<LogicalDataBundle>(),
                LogicalImportConflictStrategy.Overwrite,
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogicalImportResult(1, 0, 0, 0, []));
        var controller = Controller(repository.Object);
        var export = (FileContentResult)await controller.ExportAsync("feeds", CancellationToken.None);
        var envelope = JsonSerializer.Deserialize(
            export.FileContents,
            AppJsonSerializerContext.Default.LogicalDataExportEnvelope)!;

        var action = await controller.ImportAsync(
            new LogicalDataImportRequest(
                envelope.Data,
                envelope.Sha256,
                LogicalImportConflictStrategy.Overwrite),
            CancellationToken.None);

        var result = (LogicalImportResult)((OkObjectResult)action).Value!;
        Assert.AreEqual(1, result.Added);
    }

    [TestMethod]
    public async Task ExportRejectsARepositoryCategoryOverflow()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        repository.Setup(item => item.ExportAsync(
                It.IsAny<LogicalDataCategory>(),
                Guid.Empty,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LogicalDataExportLimitException("too many items"));

        var action = await Controller(repository.Object)
            .ExportAsync("feeds", CancellationToken.None);

        var result = Assert.IsInstanceOfType<ObjectResult>(action);
        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    [TestMethod]
    public async Task ExportRejectsABundleThatCannotFitTheImportRequestLimit()
    {
        var repository = new Mock<ILogicalDataTransferRepository>();
        repository.Setup(item => item.ExportAsync(
                LogicalDataCategory.Feeds,
                Guid.Empty,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogicalDataCategory categories, Guid _, string version, CancellationToken _) =>
            {
                var bundle = Bundle(version, categories);
                return bundle with
                {
                    Feeds =
                    [
                        bundle.Feeds[0] with
                        {
                            Name = new string('x', LogicalDataTransferLimits.MaximumPayloadBytes)
                        }
                    ]
                };
            });

        var action = await Controller(repository.Object)
            .ExportAsync("feeds", CancellationToken.None);

        var result = Assert.IsInstanceOfType<ObjectResult>(action);
        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    private static LogicalDataTransferController Controller(
        ILogicalDataTransferRepository repository) =>
        new(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static LogicalDataBundle Bundle(
        string version,
        LogicalDataCategory categories) =>
        new(
            1,
            DateTimeOffset.UtcNow,
            version,
            categories,
            categories.HasFlag(LogicalDataCategory.Feeds)
                ? [new LogicalFeed(Guid.NewGuid(), "https://example.com/feed", "Example", DateTimeOffset.UtcNow)]
                : [],
            [],
            [],
            [],
            [],
            null);

    private static string Digest(LogicalDataBundle bundle)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            bundle,
            AppJsonSerializerContext.Default.LogicalDataBundle);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
