namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PluginDeploymentTests
{
    [TestMethod]
    public void PodmanCompose_PersistsPluginPlatformUnderApplicationDataVolume()
    {
        var compose = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Deployment",
            "podman-compose.yml"));

        StringAssert.Contains(compose, "PluginPlatform__RootPath: \"/app/data/plugins\"");
        StringAssert.Contains(compose, "- appdata:/app/data");
    }
}
