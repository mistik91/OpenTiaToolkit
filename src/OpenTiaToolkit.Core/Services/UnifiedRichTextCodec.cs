using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Converts between plain text and the rich-text fragments expected by Unified HMI text APIs.
    /// </summary>
    internal static class UnifiedRichTextCodec
    {
        private static readonly Regex ParagraphRegex = new Regex(@"</p>\s*<p[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BreakRegex = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

        public static bool LooksLikeRichText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value!.Trim();
            return trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<p", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        public static string Encode(string value)
        {
            if (LooksLikeRichText(value))
            {
                return value.Trim();
            }

            var normalized = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var lines = normalized.Split('\n');

            return "<body>" + string.Concat(lines.Select(line => "<p>" + WebUtility.HtmlEncode(line) + "</p>")) + "</body>";
        }

        public static string Decode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!LooksLikeRichText(value))
            {
                return value!;
            }

            var normalized = value!.Trim();
            normalized = ParagraphRegex.Replace(normalized, "\n");
            normalized = BreakRegex.Replace(normalized, "\n");
            normalized = TagRegex.Replace(normalized, string.Empty);
            normalized = WebUtility.HtmlDecode(normalized).Replace('\u00A0', ' ');

            return normalized.Trim();
        }
    }
}
