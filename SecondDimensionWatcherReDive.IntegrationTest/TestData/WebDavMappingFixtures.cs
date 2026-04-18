using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.IntegrationTest.TestData;

internal static class WebDavMappingFixtures
{
    public static readonly DateTimeOffset FixedModified =
        new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    public static FileMapping NewMapping(string virtualPath, string physicalPath, string store = "local")
        => new(Guid.NewGuid(), Guid.NewGuid(), virtualPath, physicalPath, store);

    public static FileStoreInfo InfoFor(FileMapping m, long length)
        => new(false, m.PhysicalPath, Path.GetFileName(m.VirtualPath), length, FixedModified);
}
