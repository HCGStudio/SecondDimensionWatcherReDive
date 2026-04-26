using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.NFS;

public sealed class NfsOptions
{
    public const string SectionName = "Nfs";

    public bool Enabled { get; set; }

    [Range(0, 65535)]
    public int Port { get; set; } = 2049;

    [Required]
    public string BindAddress { get; set; } = "0.0.0.0";

    [Range(1, int.MaxValue)]
    public int LeaseSeconds { get; set; } = 90;

    [Range(1, int.MaxValue)]
    public int MaxConnections { get; set; } = 32;
}
