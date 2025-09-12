using System;
using System.IO;

namespace RagnaPH.Patching;

internal static class PatchNameUtils
{
    /// <summary>
    /// Decodes URL-encoded patch names, validates they are safe file names and
    /// returns both the decoded and canonical re-encoded representations.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when the name cannot be
    /// safely normalized.</exception>
    public static (string Decoded, string Encoded) Normalize(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("Patch file name is empty.");

            // decode any percent-encoded sequences from the server
            var decoded = Uri.UnescapeDataString(name);

            // reject names containing invalid path characters or lingering '%'
            if (decoded.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || decoded.IndexOf('%') >= 0)
                throw new InvalidDataException("Patch file name contains invalid characters.");

            // ensure the name doesn't contain path separators or traversal
            var sanitized = Path.GetFileName(decoded);
            if (!string.Equals(decoded, sanitized, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid patch file name.");

            // re-encode to a canonical form for URLs
            var encoded = Uri.EscapeDataString(sanitized);
            return (sanitized, encoded);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidDataException("Malformed patch file name.", ex);
        }
    }

    /// <summary>
    /// Normalizes a relative patch path which may contain directory segments.
    /// Each segment is validated using <see cref="Normalize"/> and the
    /// sanitized segments are re-joined using forward slashes.
    /// </summary>
    /// <param name="path">The raw path from the patch manifest.</param>
    /// <returns>The normalized path using '/' separators.</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Patch path is empty.");

        // Split on both separators and remove empty segments to handle
        // accidental leading/trailing slashes.
        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException("Patch path is empty.");

        for (int i = 0; i < segments.Length; i++)
        {
            // Validate each segment as a file name. We only keep the decoded
            // representation; encoding is performed when building the URI.
            var (decoded, _) = Normalize(segments[i]);
            segments[i] = decoded;
        }

        return string.Join("/", segments);
    }
}
