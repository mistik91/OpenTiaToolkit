using System;
using System.Text.RegularExpressions;
using OpenTiaToolkit.Core.Utilities;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcTagAddressPlanner
{
    public static string InferDataType(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;
        string normalized = TagUpdateCsvParser.NormalizeLogicalAddress(address);
        if (Regex.IsMatch(normalized, @"^%[IQM]\d+\.\d+$", RegexOptions.IgnoreCase)) return "Bool";
        if (Regex.IsMatch(normalized, @"^%[IQM]B\d+$", RegexOptions.IgnoreCase)) return "Byte";
        if (Regex.IsMatch(normalized, @"^%[IQM]W\d+$", RegexOptions.IgnoreCase)) return "Word";
        if (Regex.IsMatch(normalized, @"^%[IQM]D\d+$", RegexOptions.IgnoreCase)) return "DWord";
        return string.Empty;
    }

    public static PlcTagAddressLocation? TryParse(string address)
    {
        string normalized = TagUpdateCsvParser.NormalizeLogicalAddress(address);
        Match match = Regex.Match(normalized, @"^%(?<area>[IQM])(?:(?<size>[BWD]))?(?<byte>\d+)(?:\.(?<bit>\d+))?$", RegexOptions.IgnoreCase);
        return match.Success
            ? new PlcTagAddressLocation(match.Groups["area"].Value.ToUpperInvariant(), int.Parse(match.Groups["byte"].Value))
            : (PlcTagAddressLocation?)null;
    }
}

internal readonly struct PlcTagAddressLocation
{
    public PlcTagAddressLocation(string area, int byteNumber) { Area = area; ByteNumber = byteNumber; }
    public string Area { get; }
    public int ByteNumber { get; }
}
