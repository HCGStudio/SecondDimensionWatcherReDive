using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Inference.AI;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class TmdbRegistrationTests
{
    [TestMethod]
    public void AddTmdbMetadata_WithoutApiKey_StillResolvesTool()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTmdbMetadata(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<TmdbTool>();

        Assert.IsFalse(tool.IsConfigured);
    }
}
