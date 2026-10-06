using System;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services
{
    internal interface IPlcTagNamingStrategy
    {
        bool TryParse(string tagName, out PlcTagNameParts parts);
    }

    internal readonly struct PlcTagNameParts
    {
        public PlcTagNameParts(int pageNumber, string baseName)
        {
            PageNumber = pageNumber;
            BaseName = baseName ?? throw new ArgumentNullException(nameof(baseName));
        }

        public int PageNumber { get; }

        public string BaseName { get; }

        public bool UsesBaseNameMatching => PageNumber >= 100;
    }

    internal sealed class PageBasedPlcTagNamingStrategy : IPlcTagNamingStrategy
    {
        private static readonly Regex PageTagPattern = new Regex(
            @"^(\d+)([a-zA-Z0-9_]+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public bool TryParse(string tagName, out PlcTagNameParts parts)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                parts = default;
                return false;
            }

            var match = PageTagPattern.Match(tagName);
            if (!match.Success)
            {
                parts = default;
                return false;
            }

            parts = new PlcTagNameParts(
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value);
            return true;
        }
    }
}
