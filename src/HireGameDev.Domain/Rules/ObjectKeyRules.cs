namespace HireGameDev.Domain.Rules;

/// <summary>
/// Pure validation and generation rules for provider-independent, S3-compatible relative object keys
/// stored in PostgreSQL (e.g., <c>avatars/8f3a...d1.webp</c>, <c>logos/4c2b...a9.webp</c>).
/// Ensures zero local filesystem paths, drive letters, URLs, or traversal segments ever enter storage or DB columns.
/// </summary>
public static class ObjectKeyRules
{
    public const int MaxObjectKeyLength = 256;
    public const string CandidateAvatarsPrefix = "avatars";
    public const string StudioLogosPrefix = "logos";

    public static string BuildRandomWebpKey(string folderPrefix)
    {
        if (!TryNormalizeSegment(folderPrefix, out var normalizedPrefix))
        {
            throw new ArgumentException("Folder prefix must be a valid single object-key segment.", nameof(folderPrefix));
        }

        return $"{normalizedPrefix}/{Guid.NewGuid():N}.webp";
    }

    public static bool TryNormalize(string? rawKey, out string normalizedKey, out string? errorMessage)
    {
        normalizedKey = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            errorMessage = "Object key cannot be empty.";
            return false;
        }

        var trimmed = rawKey.Trim();
        if (trimmed.Length > MaxObjectKeyLength)
        {
            errorMessage = $"Object key cannot exceed {MaxObjectKeyLength} characters.";
            return false;
        }

        if (trimmed[0] == '/' || trimmed[^1] == '/')
        {
            errorMessage = "Object key must be relative and cannot start or end with '/'.";
            return false;
        }

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch == '\\' || ch == ':' || ch == '?' || ch == '#' || char.IsControl(ch))
            {
                errorMessage = "Object key contains disallowed path or URI characters.";
                return false;
            }
        }

        var rawSegments = trimmed.Split('/');
        if (rawSegments.Length < 2)
        {
            errorMessage = "Object key must include a logical prefix and object name (for example, 'avatars/id.webp').";
            return false;
        }

        var normalizedSegments = new string[rawSegments.Length];
        for (var i = 0; i < rawSegments.Length; i++)
        {
            if (!TryNormalizeSegment(rawSegments[i], out var segment))
            {
                errorMessage = "Object key contains an empty, traversal ('.' or '..'), or invalid segment.";
                return false;
            }

            normalizedSegments[i] = segment;
        }

        normalizedKey = string.Join('/', normalizedSegments);
        return true;
    }

    private static bool TryNormalizeSegment(string? rawSegment, out string normalizedSegment)
    {
        normalizedSegment = string.Empty;
        if (string.IsNullOrEmpty(rawSegment))
        {
            return false;
        }

        if (rawSegment is "." or "..")
        {
            return false;
        }

        for (var i = 0; i < rawSegment.Length; i++)
        {
            var ch = rawSegment[i];
            var isAllowed =
                (ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '-' ||
                ch == '_' ||
                ch == '.';

            if (!isAllowed)
            {
                return false;
            }
        }

        if (rawSegment[0] == '.' || rawSegment[^1] == '.')
        {
            return false;
        }

        normalizedSegment = rawSegment.ToLowerInvariant();
        return true;
    }
}
