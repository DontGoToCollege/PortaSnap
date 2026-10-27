using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace PortaSnap.Model;

public static class SnapJson
{
    public static readonly JsonSerializerOptions Compact = Create(false);
    public static readonly JsonSerializerOptions Indented = Create(true);

    static JsonSerializerOptions Create(bool indent)
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = indent,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new ColorJsonConverter());
        o.Converters.Add(new PointJsonConverter());
        o.Converters.Add(new RectJsonConverter());
        return o;
    }

    public static string SerializeAnnotations(List<Annotation> list) => JsonSerializer.Serialize(list, Compact);

    public static List<Annotation> DeserializeAnnotations(string json) => JsonSerializer.Deserialize<List<Annotation>>(json, Compact) ?? new();

    public static T Clone<T>(T value) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Compact), Compact)!;
}

public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Parse(reader.GetString()) ?? Colors.Transparent;

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        => writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");

    public static Color? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimStart('#');
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return null;
        return s.Length switch
        {
            8 => Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v),
            6 => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v),
            _ => null,
        };
    }
}

public sealed class PointJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var parts = (reader.GetString() ?? "0,0").Split(',');
        return new Point(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        => writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"{value.X:0.##},{value.Y:0.##}"));
}

public sealed class RectJsonConverter : JsonConverter<Rect>
{
    public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s) || s == "empty") return Rect.Empty;
        var p = s.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new Rect(p[0], p[1], Math.Max(0, p[2]), Math.Max(0, p[3]));
    }

    public override void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.IsEmpty ? "empty"
            : string.Create(CultureInfo.InvariantCulture, $"{value.X:0.##},{value.Y:0.##},{value.Width:0.##},{value.Height:0.##}"));
}
