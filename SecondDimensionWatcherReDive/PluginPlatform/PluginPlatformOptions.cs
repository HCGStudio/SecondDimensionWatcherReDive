namespace SecondDimensionWatcherReDive.PluginPlatform;

public sealed class PluginPlatformOptions
{
    public const string SectionName = "PluginPlatform";

    public string RootPath { get; set; } = "./plugin-data";
    public bool AllowUnsignedLocalPackages { get; set; }
    public long MaximumPackageBytes { get; set; } = 4 * 1024 * 1024;
    public long MaximumExpandedBytes { get; set; } = 16 * 1024 * 1024;
    public int MaximumPackageFiles { get; set; } = 128;
    public int MaximumStagedPackages { get; set; } = 32;
    public long MaximumStagedPackageBytes { get; set; } = 64 * 1024 * 1024;
    public int InvocationTimeoutMilliseconds { get; set; } = 5_000;
    public int MaximumWorkerMemoryMegabytes { get; set; } = 256;
    public int MaximumWorkerCpuMilliseconds { get; set; } = 4_000;
    public int MaximumConcurrentWorkers { get; set; } = 4;
    public int MaximumConcurrentWorkersPerPlugin { get; set; } = 1;
    public int MaximumResponseBytes { get; set; } = 2 * 1024 * 1024;
    public long MaximumPluginDataBytes { get; set; } = 64 * 1024 * 1024;
    public int MaximumPluginDataFiles { get; set; } = 1_000;
    public int MaximumPluginDataPathDepth { get; set; } = 8;
    public int CircuitBreakerFailures { get; set; } = 3;
    public int CircuitBreakerSeconds { get; set; } = 60;
    public int PreviewLifetimeMinutes { get; set; } = 30;
    public Dictionary<string, string> TrustedPublisherPublicKeys { get; set; } = new(StringComparer.Ordinal);
}
