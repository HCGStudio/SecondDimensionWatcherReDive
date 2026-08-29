using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class ManageDownloadsToolAuthorizationTests
{
    [TestMethod]
    public async Task CancelWithRemoveFile_MemberIsRejectedBeforeDownloadClientCall()
    {
        var (tool, repository, client, authorization, animation) = CreateTool();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                AccessPolicies.RecentAdministrator))
            .ReturnsAsync(AuthorizationResult.Failed());

        var result = await tool.ExecuteAsync(Arguments(
            animation.Id, removeFile: true), CancellationToken.None);

        var failure = Assert.IsInstanceOfType<ToolFailureResult>(result);
        StringAssert.Contains(failure.Error, "administrator");
        repository.Verify(repo => repo.TryBeginCancelDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(downloadClient => downloadClient.CancelDownloadTaskAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CancelWithoutRemoveFile_MemberCanCancel()
    {
        var (tool, repository, client, authorization, animation) = CreateTool();
        repository.Setup(repo => repo.TryBeginCancelDownloadAsync(
                animation.Id,
                animation.DownloadAttemptId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        client.Setup(downloadClient => downloadClient.CancelDownloadTaskAsync(
                animation.Id,
                animation.DownloadUrl,
                animation.CachedDownloadData,
                animation.AdditionalDownloadInfo,
                false,
                CancellationToken.None))
            .ReturnsAsync(new CancelDownloadResult(true, false));
        var result = await tool.ExecuteAsync(Arguments(
            animation.Id, removeFile: false), CancellationToken.None);

        var success = Assert.IsInstanceOfType<ToolSuccessResult<bool>>(result);
        Assert.IsTrue(success.Result);
        authorization.Verify(service => service.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.IsAny<object?>(),
            It.IsAny<string>()), Times.Never);
        client.Verify(downloadClient => downloadClient.CancelDownloadTaskAsync(
            animation.Id,
            animation.DownloadUrl,
            animation.CachedDownloadData,
            animation.AdditionalDownloadInfo,
            false,
            CancellationToken.None), Times.Once);
    }

    private static (
        ManageDownloadsTool Tool,
        Mock<IAnimationInfoRepository> Repository,
        Mock<IFileDownloadClient> Client,
        Mock<IAuthorizationService> Authorization,
        AnimationInfo Animation) CreateTool()
    {
        var animation = new AnimationInfo(
            Guid.NewGuid(),
            "Title",
            "Description",
            DateTimeOffset.UtcNow,
            "https://example.invalid/item.torrent",
            "torrent",
            [],
            "cached",
            true,
            default,
            default,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            0)
        {
            DownloadAttemptId = Guid.NewGuid()
        };
        var repository = new Mock<IAnimationInfoRepository>();
        repository.Setup(repo => repo.FindByIdAsync(
                animation.Id, CancellationToken.None))
            .ReturnsAsync(animation);
        var mappingRepository = new Mock<IFileMappingRepository>();
        mappingRepository.Setup(repo => repo.TryFinalizeDownloadCancellationAsync(
                animation.Id,
                animation.DownloadAttemptId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var client = new Mock<IFileDownloadClient>();
        var provider = new Mock<IFileDownloadClientProvider>();
        provider.Setup(value => value.GetClient(animation.DownloadType))
            .Returns(client.Object);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, nameof(UserRole.Member))],
                    "test"))
            }
        };
        var authorization = new Mock<IAuthorizationService>();
        var tool = new ManageDownloadsTool(
            repository.Object,
            mappingRepository.Object,
            provider.Object,
            httpContextAccessor,
            authorization.Object);
        return (tool, repository, client, authorization, animation);
    }

    private static JsonElement Arguments(Guid animationId, bool removeFile) =>
        JsonSerializer.SerializeToElement(
            new ManageDownloadsParams(
                ManageDownloadsAction.Cancel,
                animationId.ToString(),
                removeFile),
            ToolJsonOptions.Options);
}
