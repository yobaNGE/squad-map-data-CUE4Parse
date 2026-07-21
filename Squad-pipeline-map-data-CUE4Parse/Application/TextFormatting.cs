using System.Text;
using System.Text.RegularExpressions;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal static partial class TextFormatting
{
    public static string Prettify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Replace('_', ' ').Replace('-', ' ').Trim();
        var builder = new StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0 &&
                ((char.IsUpper(current) && char.IsLower(text[index - 1])) ||
                 (char.IsDigit(current) && char.IsLetter(text[index - 1]))))
                builder.Append(' ');
            builder.Append(current);
        }
        return char.ToUpperInvariant(builder[0]) + builder.ToString(1, builder.Length - 1);
    }

    public static string EnumToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var token = value.Contains("::", StringComparison.Ordinal) ? value[(value.LastIndexOf("::", StringComparison.Ordinal) + 2)..] : value;
        return token.Trim();
    }

    public static string AssetName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim('"', '\'');
        var dot = text.LastIndexOf('.');
        if (dot >= 0) text = text[(dot + 1)..];
        var slash = text.LastIndexOf('/');
        return slash >= 0 ? text[(slash + 1)..] : text;
    }

    public static int TrailingNumber(string? value, int defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var match = TrailingNumberRegex().Match(value);
        return match.Success && int.TryParse(match.Value, out var number) ? number : defaultValue;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumberRegex();
}
