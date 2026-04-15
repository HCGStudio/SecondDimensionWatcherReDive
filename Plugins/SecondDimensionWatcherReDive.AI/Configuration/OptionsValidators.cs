using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.AI.Configuration;

[OptionsValidator]
internal partial class ValidateOpenAIOptions : IValidateOptions<OpenAIOptions>;

[OptionsValidator]
internal partial class ValidateAnthropicOptions : IValidateOptions<AnthropicOptions>;
