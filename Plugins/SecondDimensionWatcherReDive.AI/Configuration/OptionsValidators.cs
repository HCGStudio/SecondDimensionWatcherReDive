using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.AI.Configuration;

[OptionsValidator]
internal partial class ValidateOpenAiOptions : IValidateOptions<OpenAiOptions>;

[OptionsValidator]
internal partial class ValidateAnthropicOptions : IValidateOptions<AnthropicOptions>;
