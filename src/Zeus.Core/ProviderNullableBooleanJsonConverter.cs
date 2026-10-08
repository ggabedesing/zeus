using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeus.Core;

/// <summary>Accepts only actual JSON booleans from providers; malformed values remain unknown.</summary>
public sealed class ProviderNullableBooleanJsonConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            _ => SkipAndReturnUnknown(ref reader)
        };
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is { } boolean) writer.WriteBooleanValue(boolean);
        else writer.WriteNullValue();
    }

    private static bool? SkipAndReturnUnknown(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}
