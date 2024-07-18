using Microsoft.Extensions.Hosting;

namespace SecondDimensionWatcherReDive.AppHost;

public static class YarnExtensions
{
    public static IResourceBuilder<NodeAppResource> AddYarnApp(
        this IDistributedApplicationBuilder builder,
        string name,
        string workingDirectory,
        string scriptName = "start",
        string[]? args = null)
    {
        string[] allArgs = args is { Length: > 0 }
            ? ["run", scriptName, "--", .. args]
            : ["run", scriptName];

        workingDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, workingDirectory));
        var resource = new NodeAppResource(name, "yarn", workingDirectory);

        return builder.AddResource(resource)
            .WithNodeDefaults()
            .WithArgs(allArgs);
    }
    
    private static IResourceBuilder<NodeAppResource> WithNodeDefaults(this IResourceBuilder<NodeAppResource> builder) =>
        builder.WithOtlpExporter()
            .WithEnvironment(
                "NODE_ENV",
                builder.ApplicationBuilder.Environment.IsDevelopment() ? "development" : "production");
}