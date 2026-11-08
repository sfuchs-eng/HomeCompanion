namespace HomeCompanion.Integrations.Sonos;

internal static class SonosValidation
{
    public static bool IsValidMediaUrl(string? mediaUrl)
    {
        return Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static string NormalizePlayerId(string? value, string fallback)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        sanitized = sanitized.Replace("uuid:", string.Empty, StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Trim();
        return sanitized.ToLowerInvariant();
    }
}
