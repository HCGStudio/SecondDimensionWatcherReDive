using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Inference.AI.Configuration;

[OptionsValidator]
internal partial class ValidateInferenceOptions : IValidateOptions<InferenceOptions>;
