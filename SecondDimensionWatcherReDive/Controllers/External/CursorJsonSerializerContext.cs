using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers.External;

// Cursors retain their original PascalCase representation independently of MVC's Web options.
[JsonSerializable(typeof(AnimationCatalogCursor))]
[JsonSerializable(typeof(AnimationInfoCursor))]
internal partial class CursorJsonSerializerContext : JsonSerializerContext;
