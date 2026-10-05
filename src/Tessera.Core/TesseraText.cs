namespace Tessera.Core;

internal static class TesseraText
{
    public static string LayerName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var buffer = new char[Math.Min(value.Length, 100)];
        var count = 0;
        foreach (var c in value.Trim())
        {
            if (count == buffer.Length)
            {
                break;
            }

            buffer[count++] = c is ':' or '\\' or '/' or '{' or '}' or ';' or '\t' or '\r' or '\n'
                ? '_'
                : c;
        }

        var name = new string(buffer, 0, count).Trim().Trim('_');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    public static string FileToken(string? value, string fallback)
    {
        var layer = LayerName(value, fallback);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            layer = layer.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(layer) ? fallback : layer;
    }

    public static string? UserKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var buffer = new char[Math.Min(key.Length, 200)];
        var count = 0;
        foreach (var c in key.Trim())
        {
            if (count == buffer.Length)
            {
                break;
            }

            buffer[count++] = c is '\r' or '\n' or '\t' or '=' ? '_' : c;
        }

        var sanitized = new string(buffer, 0, count).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    public static string? UserValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (trimmed.Length > 1024)
        {
            trimmed = trimmed.Substring(0, 1024);
        }

        return trimmed.Length == 0 ? null : trimmed;
    }
}
