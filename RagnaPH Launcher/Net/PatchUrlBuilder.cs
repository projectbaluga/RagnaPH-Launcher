using System;
using System.Linq;

namespace RagnaPH.Launcher.Net
{
    internal static class PatchUrlBuilder
    {
        public static Uri Build(Uri baseUri, string relativePath)
        {
            if (baseUri == null) throw new ArgumentNullException(nameof(baseUri));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Empty relative path", nameof(relativePath));

            var parts = relativePath.Trim().Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Uri.EscapeDataString(p));

            var encoded = string.Join("/", parts);
            var normalizedBase = baseUri.ToString().EndsWith("/") ? baseUri : new Uri(baseUri + "/");
            return new Uri(normalizedBase, encoded);
        }
    }
}

