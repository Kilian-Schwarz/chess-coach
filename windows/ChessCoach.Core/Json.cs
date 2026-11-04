using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public static class ChessCoachJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new IsoDateConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    public static string FormatDate(DateTime d)
    {
        var u = d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
        return u.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}

public sealed class IsoDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? throw new JsonException("Missing date");
        return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ChessCoachJson.FormatDate(value));
}

public abstract class RawEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract string ToRaw(T value);
    protected abstract bool TryFromRaw(string raw, out T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? "";
        if (TryFromRaw(s, out var v)) return v;
        throw new JsonException($"Unknown value '{s}' for {typeof(T).Name}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(ToRaw(value));
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options);
    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(ToRaw(value));
}

public static class Fmt
{
    public static string Decimal1(double v) =>
        Math.Round((decimal)v, 1, MidpointRounding.ToEven).ToString("0.0", CultureInfo.InvariantCulture);

    public static string Round0(double v) => Math.Round(v, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
}
