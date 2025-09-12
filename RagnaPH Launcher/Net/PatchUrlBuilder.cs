using System;
using System.Linq;

namespace RagnaPH.Launcher.Net {
    internal static class PatchUrlBuilder {
        public static Uri Build(Uri baseUri, string relativePath) {
            if (baseUri == null) throw new ArgumentNullException(nameof(baseUri));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Empty relative path", nameof(relativePath));

            // Normalize and encode each segment (never encode the whole string at once)
            var encoded = string.Join("/",
                relativePath.Trim()
                            .Replace('\\', '/')
                            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(Uri.EscapeDataString));

            // Ensure base ends with '/'
            var normalizedBase = baseUri.ToString().EndsWith("/") ? baseUri : new Uri(baseUri + "/");
            return new Uri(normalizedBase, encoded);
        }
    }
}

