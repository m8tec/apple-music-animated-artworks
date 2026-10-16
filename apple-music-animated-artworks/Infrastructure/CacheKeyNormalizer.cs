using System.Text;
using System.Text.RegularExpressions;

namespace AnimatedArtworks.Infrastructure;

public static partial class CacheKeyNormalizer
{
    // A bracketed note like "(Deluxe Version)" or "[Remastered]", or a trailing note like " - EP".
    [GeneratedRegex(@"\s*\([^()]*\)|\s*\[[^\[\]]*\]|\s+[-–—]\s+[^()\[\]]+$", RegexOptions.NonBacktracking)]
    private static partial Regex EditionSuffixRegex();

    [GeneratedRegex(@"\b(?:deluxe|edition|remaster|remastered|expanded|anniversary|bonus|reissue|ep|single|explicit|clean)\b",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex EditionKeywordRegex();

    // A live recording is a different album, even if it is labeled as an edition.
    [GeneratedRegex(@"\blive\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex LiveKeywordRegex();

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(input.Length);
        foreach (char character in input)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    /// <summary>
    /// Normalizes an album title without its edition notes, so "Plastic Beach (Deluxe Version)" and
    /// "Plastic Beach" share a key, while "Time" and "Time Tour Live" do not.
    /// </summary>
    public static string NormalizeAlbumTitle(string? album)
    {
        string normalized = Normalize(StripEditionSuffixes(album));

        // A title that consists of nothing but an edition note keeps it.
        return normalized.Length > 0 ? normalized : Normalize(album);
    }

    public static string StripEditionSuffixes(string? album)
    {
        if (string.IsNullOrWhiteSpace(album))
        {
            return string.Empty;
        }

        // Repeat, because a trailing " - EP" only becomes the end of the title once a bracketed note after it is gone.
        string current = album;
        while (true)
        {
            string stripped = EditionSuffixRegex().Replace(current, match => IsEditionNote(match.Value) ? string.Empty : match.Value);
            if (stripped == current)
            {
                return stripped;
            }

            current = stripped;
        }
    }

    public static bool IsEditionNote(string text)
    {
        return EditionKeywordRegex().IsMatch(text) && !LiveKeywordRegex().IsMatch(text);
    }

    public static string BuildMetadataKey(string? artist, string? album)
    {
        return $"{Normalize(artist)}|{Normalize(album)}";
    }

    public static bool HasAnimatedArtwork(string? m3U8Url, string? m3U8UrlTall)
    {
        return (m3U8Url != null && m3U8Url != "NONE") || (m3U8UrlTall != null && m3U8UrlTall != "NONE");
    }
}
