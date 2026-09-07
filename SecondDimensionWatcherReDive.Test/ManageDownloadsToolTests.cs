using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ManageDownloadsToolTests
{
    [TestMethod]
    public async Task PauseRejectedByClientReturnsToolFailure()
    {
        var fixture = new Fixture();
        fixture.Client
            .Setup(client => client.PauseDownloadTaskAsync(
                fixture.Info.Id,
                fixture.Info.DownloadUrl,
                fixture.Info.CachedDownloadData,
                fixture.Info.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.ExecuteAsync("pause");

        Assert.IsInstanceOfType<ToolFailureResult>(result);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(((ToolFailureResult)result).Error, "pause");
    }

    [TestMethod]
    public async Task ResumeRejectedByClientReturnsToolFailure()
    {
        var fixture = new Fixture();
        fixture.Client
            .Setup(client => client.ResumeDownloadTaskAsync(
                fixture.Info.Id,
                fixture.Info.DownloadUrl,
                fixture.Info.CachedDownloadData,
                fixture.Info.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.ExecuteAsync("resume");

        Assert.IsInstanceOfType<ToolFailureResult>(result);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(((ToolFailureResult)result).Error, "resume");
    }

    [TestMethod]
    public async Task CancelRejectedByClientReturnsToolFailureAndDoesNotFinalize()
    {
        var fixture = new Fixture();
        fixture.AnimationRepository
            .Setup(repository => repository.TryBeginCancelDownloadAsync(
                fixture.Info.Id,
                fixture.Info.DownloadAttemptId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Client
            .Setup(client => client.CancelDownloadTaskAsync(
                fixture.Info.Id,
                fixture.Info.DownloadUrl,
                fixture.Info.CachedDownloadData,
                fixture.Info.AdditionalDownloadInfo,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CancelDownloadResult(false, false));

        var result = await fixture.ExecuteAsync("cancel");

        Assert.IsInstanceOfType<ToolFailureResult>(result);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(((ToolFailureResult)result).Error, "cancel");
        fixture.MappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Info = new AnimationInfo(
                Guid.NewGuid(),
                "Test animation",
                "Description",
                DateTimeOffset.UtcNow,
                "https://example.test/item.torrent",
                "test",
                [],
                "hash",
                true,
                DateTimeOffset.UtcNow,
                default,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                0,
                DownloadAttemptId: Guid.NewGuid());
            AnimationRepository
                .Setup(repository => repository.FindByIdAsync(
                    Info.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Info);
            ClientProvider
                .Setup(provider => provider.GetClient(Info.DownloadType))
                .Returns(Client.Object);
            Tool = new ManageDownloadsTool(
                AnimationRepository.Object,
                MappingRepository.Object,
                ClientProvider.Object);
        }

        public AnimationInfo Info { get; }
        public Mock<IAnimationInfoRepository> AnimationRepository { get; } = new();
        public Mock<IFileMappingRepository> MappingRepository { get; } = new();
        public Mock<IFileDownloadClient> Client { get; } = new();
        public Mock<IFileDownloadClientProvider> ClientProvider { get; } = new();
        private ManageDownloadsTool Tool { get; }

        public async Task<SecondDimensionWatcherReDive.Framework.AI.IToolResult> ExecuteAsync(
            string action)
        {
            using var document = JsonDocument.Parse(
                $$"""{"action":"{{action}}","animation_id":"{{Info.Id}}"}""");
            return await Tool.ExecuteAsync(document.RootElement, CancellationToken.None);
        }
    }
}
