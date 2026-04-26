using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.NFS;

[OptionsValidator]
internal partial class ValidateNfsOptions : IValidateOptions<NfsOptions>;
