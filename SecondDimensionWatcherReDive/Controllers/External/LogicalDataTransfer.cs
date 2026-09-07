using System.ComponentModel.DataAnnotations;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record LogicalDataExportEnvelope(
    LogicalDataBundle Data,
    string Sha256);

internal sealed record LogicalDataImportRequest(
    [property: Required] LogicalDataBundle? Data,
    [property: Required] string? Sha256,
    [property: Required] LogicalImportConflictStrategy? ConflictStrategy);
