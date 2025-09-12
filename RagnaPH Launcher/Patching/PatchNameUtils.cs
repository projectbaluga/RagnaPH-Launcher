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
}
