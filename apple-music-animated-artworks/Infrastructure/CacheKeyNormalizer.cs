using System.Text;

namespace AnimatedArtworks.Infrastructure;

public static class CacheKeyNormalizer
{
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

    public static string BuildMetadataKey(string? artist, string? album)
    {
        return $"{Normalize(artist)}|{Normalize(album)}";
    }

    public static bool HasAnimatedArtwork(string? m3U8Url, string? m3U8UrlTall)
    {
        return (m3U8Url != null && m3U8Url != "NONE") || (m3U8UrlTall != null && m3U8UrlTall != "NONE");
    }
}
