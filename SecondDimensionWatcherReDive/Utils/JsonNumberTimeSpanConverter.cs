using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Utils;

public class JsonNumberTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException();
        if (typeToConvert != typeof(TimeSpan))
            throw new JsonException();

        return TimeSpan.FromSeconds(reader.GetDouble());
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.TotalSeconds);
    }
}