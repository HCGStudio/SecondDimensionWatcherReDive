using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FileMapperRegexInferenceTests
{
    private const long ExpectedStateVersion = 7;

    private static readonly string[] VideoFileNames =
    [
        "Anime - 01.mkv",
        "Anime - 02.mkv"
    ];

    [TestMethod]
    public async Task MapDownloadAsync_RegexMatches_DoesNotCallInferenceEngine()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - (?<episode>\d+)\.mkv$",
                    null,
                    DateTimeOffset.UtcNow)
            ]);

        await fixture.Mapper.MapDownloadAsync(fixture.AnimationInfoId, CancellationToken.None);

        fixture.InferenceEngine.Verify(engine => engine.InferFileNamesAsync(
            It.IsAny<FileNameInferenceRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            fixture.AnimationInfoId,
            ExpectedStateVersion,
            "test-store",
            "/downloads/anime",
            It.Is<IReadOnlyList<FileMapping>>(mappings =>
                mappings.Count == 2
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E01.mkv"))
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E02.mkv"))),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task MapDownloadAsync_ConflictingRules_UsesRepositoryPriorityOrder()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - 0?(?<episode>[12])\.mkv$",
                    "Preferred specific rule",
                    DateTimeOffset.UtcNow),
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - (?<episode>\d)\d\.mkv$",
                    "Broader older rule",
                    DateTimeOffset.UtcNow.AddDays(-1))
            ]);

        await fixture.Mapper.MapDownloadAsync(fixture.AnimationInfoId, CancellationToken.None);

        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            fixture.AnimationInfoId,
            ExpectedStateVersion,
            "test-store",
            "/downloads/anime",
            It.Is<IReadOnlyList<FileMapping>>(mappings =>
                mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E01.mkv"))
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E02.mkv"))
                && mappings.All(mapping => !mapping.VirtualPath.Contains("E00", StringComparison.Ordinal))),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task MapDownloadAsync_RegexMiss_BatchesFilesWithRuleCreationEnabled()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Different release (?<episode>\d+)\.mkv$",
                    null,
                    DateTimeOffset.UtcNow)
            ]);

        FileNameInferenceRequest? capturedRequest = null;
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.IsAny<FileNameInferenceRequest>(),
                CancellationToken.None))
            .Callback<FileNameInferenceRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[0], 1, 1),
                new FileNameInferenceResult(VideoFileNames[1], 1, 2)
            ]);

        await fixture.Mapper.MapDownloadAsync(fixture.AnimationInfoId, CancellationToken.None);

        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.AllowRegexRuleCreation);
        Assert.AreEqual(fixture.AnimationId, capturedRequest.AnimationId);
        CollectionAssert.AreEqual(
            VideoFileNames,
            capturedRequest.Files.Select(file => file.FilePath).ToArray());
        fixture.InferenceEngine.Verify(engine => engine.InferFileNamesAsync(
            It.IsAny<FileNameInferenceRequest>(),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task MapDownloadAsync_PartialRegexMatch_ToolContextStillContainsWholeBatch()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - (?<episode>01)\.mkv$",
                    null,
                    DateTimeOffset.UtcNow)
            ]);

        FileNameInferenceRequest? capturedRequest = null;
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.IsAny<FileNameInferenceRequest>(),
                CancellationToken.None))
            .Callback<FileNameInferenceRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[1], 1, 2)
            ]);

        await fixture.Mapper.MapDownloadAsync(fixture.AnimationInfoId, CancellationToken.None);

        Assert.IsNotNull(capturedRequest);
        CollectionAssert.AreEqual(
            VideoFileNames,
            capturedRequest.Files.Select(file => file.FilePath).ToArray());
        CollectionAssert.AreEqual(
            new[] { VideoFileNames[1] },
            capturedRequest.TargetFilePaths!.ToArray());
        Assert.AreEqual(1, capturedRequest.ExistingResults!.Count);
        Assert.AreEqual(VideoFileNames[0], capturedRequest.ExistingResults[0].FilePath);
        Assert.AreEqual(1, capturedRequest.ExistingResults[0].Episode);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAiAsync_SkipsRulesAndDisablesRuleCreation()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - (?<episode>\d+)\.mkv$",
                    null,
                    DateTimeOffset.UtcNow)
            ]);

        FileNameInferenceRequest? capturedRequest = null;
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.IsAny<FileNameInferenceRequest>(),
                CancellationToken.None))
            .Callback<FileNameInferenceRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[0], 1, 11),
                new FileNameInferenceResult(VideoFileNames[1], 1, 12)
            ]);

        var result = await fixture.Mapper.ReidentifyFilesWithAiAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsTrue(result);
        Assert.IsNotNull(capturedRequest);
        Assert.IsFalse(capturedRequest.AllowRegexRuleCreation);
        CollectionAssert.AreEqual(
            VideoFileNames,
            capturedRequest.Files.Select(file => file.FilePath).ToArray());
        fixture.RuleRepository.Verify(repository => repository.GetForAnimationAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.InferenceEngine.Verify(engine => engine.InferFileNamesAsync(
            It.IsAny<FileNameInferenceRequest>(),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAiAsync_NoResults_PreservesExistingMappings()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameRegexRule(
                    Guid.NewGuid(),
                    fixture.AnimationId,
                    @"^Anime - (?<episode>\d+)\.mkv$",
                    null,
                    DateTimeOffset.UtcNow)
            ]);
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.Is<FileNameInferenceRequest>(request => !request.AllowRegexRuleCreation),
                CancellationToken.None))
            .ReturnsAsync(Array.Empty<FileNameInferenceResult>());

        var result = await fixture.Mapper.ReidentifyFilesWithAiAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsFalse(result);
        fixture.RuleRepository.Verify(repository => repository.GetForAnimationAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.InferenceEngine.Verify(engine => engine.InferFileNamesAsync(
            It.Is<FileNameInferenceRequest>(request => !request.AllowRegexRuleCreation),
            CancellationToken.None), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            It.IsAny<Guid>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAiAsync_PartialResults_PreservesExistingMappings()
    {
        var fixture = CreateFixture();
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.Is<FileNameInferenceRequest>(request => !request.AllowRegexRuleCreation),
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[0], 1, 1)
            ]);

        var result = await fixture.Mapper.ReidentifyFilesWithAiAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsFalse(result);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            It.IsAny<Guid>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAiAsync_ExistingOwnPaths_DoesNotAddCollisionSuffixes()
    {
        var fixture = CreateFixture();
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.Is<FileNameInferenceRequest>(request => !request.AllowRegexRuleCreation),
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[0], 1, 1),
                new FileNameInferenceResult(VideoFileNames[1], 1, 2)
            ]);
        fixture.FileMappingRepository
            .Setup(repository => repository.FindByVirtualPathAsync(
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync((string path, CancellationToken _) => new FileMapping(
                Guid.NewGuid(),
                fixture.AnimationInfoId,
                path,
                "/old/file.mkv",
                "test-store"));

        var result = await fixture.Mapper.ReidentifyFilesWithAiAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsTrue(result);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            fixture.AnimationInfoId,
            ExpectedStateVersion,
            "test-store",
            "/downloads/anime",
            It.Is<IReadOnlyList<FileMapping>>(mappings =>
                mappings.Count == 2
                && mappings.All(mapping => !mapping.VirtualPath.Contains(" (2)", StringComparison.Ordinal))),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAiAsync_DownloadChangedBeforeReplace_ReturnsFalse()
    {
        var fixture = CreateFixture();
        fixture.InferenceEngine
            .Setup(engine => engine.InferFileNamesAsync(
                It.Is<FileNameInferenceRequest>(request => !request.AllowRegexRuleCreation),
                CancellationToken.None))
            .ReturnsAsync(
            [
                new FileNameInferenceResult(VideoFileNames[0], 1, 1),
                new FileNameInferenceResult(VideoFileNames[1], 1, 2)
            ]);
        fixture.FileMappingRepository
            .Setup(repository => repository.ReplaceForAnimationInfoAsync(
                fixture.AnimationInfoId,
                ExpectedStateVersion,
                "test-store",
                "/downloads/anime",
                It.IsAny<IReadOnlyList<FileMapping>>(),
                CancellationToken.None))
            .ReturnsAsync(false);

        var result = await fixture.Mapper.ReidentifyFilesWithAiAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task PreviewDownloadAsync_RegexMiss_DoesNotCallAiOrReplaceMappings()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetForAnimationAsync(
                fixture.AnimationId,
                CancellationToken.None))
            .ReturnsAsync(Array.Empty<FileNameRegexRule>());

        var preview = await fixture.Mapper.PreviewDownloadAsync(
            fixture.Info,
            CancellationToken.None);

        Assert.IsNotNull(preview);
        Assert.AreEqual(2, preview.Mappings.Count);
        Assert.IsTrue(preview.Mappings.All(mapping =>
            mapping.VirtualPath.StartsWith("/unknown/", StringComparison.Ordinal)));
        CollectionAssert.Contains(preview.Warnings.ToList(), "unresolvedFiles");
        fixture.InferenceEngine.Verify(engine => engine.InferFileNamesAsync(
            It.IsAny<FileNameInferenceRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            It.IsAny<Guid>(),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task MapDownloadAsync_SingleEpisode_AssociatesSeparatedAndGenericSidecars()
    {
        var entries = new[]
        {
            new FileStoreInfo(false, "/downloads/anime/Anime - 01.mkv", "Anime - 01.mkv"),
            new FileStoreInfo(false, "/downloads/anime/Anime - 01 zh-Hans.ass", "Anime - 01 zh-Hans.ass"),
            new FileStoreInfo(false, "/downloads/anime/Chinese.srt", "Chinese.srt")
        };
        var fixture = CreateFixture(entries, episode: 5);

        var result = await fixture.Mapper.MapDownloadAsync(
            fixture.AnimationInfoId,
            CancellationToken.None);

        Assert.IsTrue(result);
        fixture.FileMappingRepository.Verify(repository => repository.ReplaceForAnimationInfoAsync(
            fixture.AnimationInfoId,
            ExpectedStateVersion,
            "test-store",
            "/downloads/anime",
            It.Is<IReadOnlyList<FileMapping>>(mappings =>
                mappings.Count == 3
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E05.mkv"))
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E05 zh-Hans.ass"))
                && mappings.Any(mapping => mapping.VirtualPath.EndsWith("Anime S01E05.Chinese.srt"))
                && mappings.All(mapping => !mapping.VirtualPath.StartsWith("/unknown/"))),
            CancellationToken.None), Times.Once);
    }

    private static Fixture CreateFixture(
        IEnumerable<FileStoreInfo>? fileEntries = null,
        int? episode = null)
    {
        var animationInfoId = Guid.NewGuid();
        var animationId = Guid.NewGuid();
        var animation = new Animation(animationId, "123", "Anime", "Anime", null);
        var group = new AnimationGroup(Guid.NewGuid(), "TestGroup");
        var info = new AnimationInfo(
            animationInfoId,
            "Anime batch",
            "Batch release",
            DateTimeOffset.UtcNow,
            "https://example.test/anime.torrent",
            "torrent",
            Array.Empty<byte>(),
            "hash",
            true,
            default,
            default,
            true,
            "test-store",
            "/downloads/anime",
            1,
            episode,
            group,
            animation,
            true,
            0,
            StateVersion: ExpectedStateVersion);

        var animationInfoRepository = new Mock<IAnimationInfoRepository>();
        animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                animationInfoId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(info);

        var fileMappingRepository = new Mock<IFileMappingRepository>();
        fileMappingRepository
            .Setup(repository => repository.VirtualPathExistsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fileMappingRepository
            .Setup(repository => repository.ReplaceForAnimationInfoAsync(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FileMapping>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var fileStore = new FakeFileStore(
            fileEntries ?? VideoFileNames.Select(fileName => new FileStoreInfo(
                false,
                $"/downloads/anime/{fileName}",
                fileName)));
        var fileStoreProvider = new Mock<IFileStoreProvider>();
        fileStoreProvider
            .Setup(provider => provider.GetClient("test-store"))
            .Returns(fileStore);

        var ruleRepository = new Mock<IFileNameRegexRuleRepository>();
        var inferenceEngine = new Mock<IInferenceEngine>();
        var mapper = new FileMapper(
            animationInfoRepository.Object,
            fileMappingRepository.Object,
            ruleRepository.Object,
            fileStoreProvider.Object,
            NullLogger<FileMapper>.Instance,
            inferenceEngine.Object);

        return new Fixture(
            animationInfoId,
            animationId,
            info,
            mapper,
            fileMappingRepository,
            ruleRepository,
            inferenceEngine);
    }

    private sealed record Fixture(
        Guid AnimationInfoId,
        Guid AnimationId,
        AnimationInfo Info,
        FileMapper Mapper,
        Mock<IFileMappingRepository> FileMappingRepository,
        Mock<IFileNameRegexRuleRepository> RuleRepository,
        Mock<IInferenceEngine> InferenceEngine);

    private sealed class FakeFileStore(IEnumerable<FileStoreInfo> entries) : IFileStore
    {
        private readonly IReadOnlyList<FileStoreInfo> _entries = entries.ToList();

        public string Name => "test-store";

        public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStoreInfo> FileInfoAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path)
        {
            if (path != "/downloads/anime") yield break;

            foreach (var entry in _entries)
            {
                await Task.Yield();
                yield return entry;
            }
        }
    }
}
