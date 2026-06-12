using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to audit nearest-label matching before bulk HMI edits.
    /// </summary>
    public class AuditHmiLabelProximityCommand : ICommand
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public string Name => "audit-hmi-label-proximity";

        public string Description => "Creates an auditable nearest-label mapping report from an exported Unified HMI JSON file";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("input-path", CommandParameterType.Path, "Unified HMI JSON export file", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "Optional screen path to audit", required: false),
                new CommandParameter("target-items", CommandParameterType.StringArray, "Optional target item names", required: false),
                new CommandParameter("target-types", CommandParameterType.StringArray, "Optional target item types; defaults to HmiFaceplateContainer", required: false),
                new CommandParameter("label-texts", CommandParameterType.StringArray, "Optional exact label text values to include", required: false),
                new CommandParameter("label-types", CommandParameterType.StringArray, "Optional label item types; defaults to HmiText,HmiLabel", required: false),
                new CommandParameter("max-candidates", CommandParameterType.Integer, "Number of candidates to include per target", required: false, defaultValue: 5),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON report output path", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var inputPath = args.GetString("input-path");
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: input-path");
            }

            if (!File.Exists(inputPath))
            {
                return CommandResult.CreateFailure($"Unified HMI JSON input file not found: {inputPath}");
            }

            HmiJsonDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<HmiJsonDocument>(File.ReadAllText(inputPath), JsonOptions);
            }
            catch (Exception ex)
            {
                return CommandResult.CreateFailure($"Failed to read Unified HMI JSON input file: {ex.Message}");
            }

            if (document == null)
            {
                return CommandResult.CreateFailure($"Unified HMI JSON input file is empty or invalid: {inputPath}");
            }

            var report = BuildReport(document, inputPath, args);
            if (args.Has("output-path"))
            {
                var outputPath = args.GetString("output-path");
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return CommandResult.CreateFailure("Parameter output-path must contain a JSON file path.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
                File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions));
                report.OutputPath = outputPath;
            }

            return await Task.FromResult(CommandResult.CreateSuccess(
                report,
                $"HMI label proximity audit completed: {report.MappingCount} mapping(s), {report.DuplicateLabelMatchCount} duplicate label match(es)."));
        }

        private static HmiLabelProximityAuditReport BuildReport(HmiJsonDocument document, string inputPath, CommandArgs args)
        {
            var screenPath = args.Has("screen-path") ? args.GetString("screen-path") : null;
            var targetItems = ToHashSet(args.GetStringArray("target-items"));
            var targetTypes = ToHashSet(args.GetStringArray("target-types"));
            if (targetTypes.Count == 0)
            {
                targetTypes.Add("HmiFaceplateContainer");
            }

            var labelTexts = ToHashSet(args.GetStringArray("label-texts"));
            var labelTypes = ToHashSet(args.GetStringArray("label-types"));
            if (labelTypes.Count == 0)
            {
                labelTypes.Add("HmiText");
                labelTypes.Add("HmiLabel");
            }

            var maxCandidates = Math.Max(1, args.Has("max-candidates") ? args.GetInt("max-candidates", 5) : 5);
            var report = new HmiLabelProximityAuditReport
            {
                InputPath = inputPath,
                ScreenPath = screenPath,
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                TargetTypes = targetTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                LabelTypes = labelTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                LabelTexts = labelTexts.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
            };

            foreach (var screen in document.Screens.Where(screen =>
                string.IsNullOrWhiteSpace(screenPath) ||
                string.Equals(screen.Path, screenPath, StringComparison.OrdinalIgnoreCase)))
            {
                var labels = screen.Items
                    .Where(item => labelTypes.Contains(item.Type))
                    .Select(item => HmiProximityItem.TryCreate(item, screen.Path, isLabel: true))
                    .Where(item => item != null)
                    .Cast<HmiProximityItem>()
                    .Where(item => labelTexts.Count == 0 || labelTexts.Contains(item.Text ?? string.Empty))
                    .ToList();

                var targets = screen.Items
                    .Where(item => targetTypes.Contains(item.Type))
                    .Where(item => targetItems.Count == 0 || targetItems.Contains(item.Name))
                    .Select(item => HmiProximityItem.TryCreate(item, screen.Path, isLabel: false))
                    .Where(item => item != null)
                    .Cast<HmiProximityItem>()
                    .ToList();

                foreach (var target in targets)
                {
                    var candidates = labels
                        .Select(label => HmiLabelCandidate.FromValues(
                            target.CenterX,
                            target.CenterY,
                            label.Name,
                            label.Type,
                            label.Text,
                            label.CenterX,
                            label.CenterY))
                        .OrderBy(candidate => candidate.Distance)
                        .ThenBy(candidate => candidate.LabelItemName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var selected = candidates.FirstOrDefault();

                    report.Mappings.Add(new HmiLabelProximityMapping
                    {
                        ScreenPath = screen.Path,
                        TargetItemName = target.Name,
                        TargetType = target.Type,
                        TargetLeft = target.Left,
                        TargetTop = target.Top,
                        TargetCenterX = target.CenterX,
                        TargetCenterY = target.CenterY,
                        MatchedLabelItemName = selected?.LabelItemName,
                        MatchedLabelText = selected?.LabelText,
                        MatchedLabelType = selected?.LabelType,
                        Distance = selected?.Distance,
                        DeltaX = selected?.DeltaX,
                        DeltaY = selected?.DeltaY,
                        CandidateCount = candidates.Count,
                        Candidates = candidates.Take(maxCandidates).ToList(),
                        SkippedCandidateCount = Math.Max(0, candidates.Count - maxCandidates)
                    });
                }
            }

            foreach (var duplicateGroup in report.Mappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.MatchedLabelItemName))
                .GroupBy(mapping => mapping.MatchedLabelItemName!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var mapping in duplicateGroup)
                {
                    mapping.DuplicateLabelMatch = true;
                }
            }

            report.MappingCount = report.Mappings.Count;
            report.DuplicateLabelMatchCount = report.Mappings.Count(mapping => mapping.DuplicateLabelMatch);
            report.UnmatchedTargetCount = report.Mappings.Count(mapping => string.IsNullOrWhiteSpace(mapping.MatchedLabelItemName));
            return report;
        }

        private static HashSet<string> ToHashSet(IEnumerable<string> values)
        {
            return new HashSet<string>(
                values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private sealed class HmiProximityItem
        {
            private HmiProximityItem(string screenPath, HmiJsonItem item, double left, double top, double width, double height, string? text)
            {
                ScreenPath = screenPath;
                Name = item.Name;
                Type = item.Type;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Text = text;
            }

            public string ScreenPath { get; }
            public string Name { get; }
            public string Type { get; }
            public double Left { get; }
            public double Top { get; }
            public double Width { get; }
            public double Height { get; }
            public string? Text { get; }
            public double CenterX => Left + Width / 2.0;
            public double CenterY => Top + Height / 2.0;

            public static HmiProximityItem? TryCreate(HmiJsonItem item, string screenPath, bool isLabel)
            {
                if (!TryReadDouble(item, "Left", out var left) ||
                    !TryReadDouble(item, "Top", out var top))
                {
                    return null;
                }

                TryReadDouble(item, "Width", out var width);
                TryReadDouble(item, "Height", out var height);
                var text = isLabel ? ReadPlainText(item) : null;
                if (isLabel && string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return new HmiProximityItem(screenPath, item, left, top, width, height, text);
            }

            private static bool TryReadDouble(HmiJsonItem item, string propertyName, out double value)
            {
                value = 0;
                return item.Properties.TryGetValue(propertyName, out var rawValue) &&
                       double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static string? ReadPlainText(HmiJsonItem item)
            {
                var preferredProperties = new[] { "Text", "Caption", "Content", "Value" };
                foreach (var propertyName in preferredProperties)
                {
                    if (item.Properties.TryGetValue(propertyName, out var rawValue))
                    {
                        var text = NormalizeText(rawValue);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }

                foreach (var tag in item.Dynamizations?
                    .Select(dynamization => dynamization.Binding?.Tag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag)) ?? Enumerable.Empty<string>())
                {
                    var match = Regex.Match(tag!, @"(?<![A-Z0-9])V\d+(?![A-Z0-9])", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return match.Value.ToUpperInvariant();
                    }
                }

                return null;
            }

            private static string? NormalizeText(string? rawValue)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return null;
                }

                var trimmed = rawValue!.Trim();
                if (trimmed.StartsWith("<", StringComparison.Ordinal))
                {
                    try
                    {
                        var document = XDocument.Parse(trimmed);
                        var text = string.Concat(document.DescendantNodes().OfType<XText>().Select(node => node.Value)).Trim();
                        return string.IsNullOrWhiteSpace(text) ? null : text;
                    }
                    catch
                    {
                        return trimmed;
                    }
                }

                return trimmed;
            }
        }
    }

    public sealed class HmiLabelProximityAuditReport
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public string InputPath { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public string? ScreenPath { get; set; }
        public List<string> TargetTypes { get; set; } = new List<string>();
        public List<string> LabelTypes { get; set; } = new List<string>();
        public List<string> LabelTexts { get; set; } = new List<string>();
        public int MappingCount { get; set; }
        public int DuplicateLabelMatchCount { get; set; }
        public int UnmatchedTargetCount { get; set; }
        public List<HmiLabelProximityMapping> Mappings { get; set; } = new List<HmiLabelProximityMapping>();
    }

    public sealed class HmiLabelProximityMapping
    {
        public string ScreenPath { get; set; } = string.Empty;
        public string TargetItemName { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public double TargetLeft { get; set; }
        public double TargetTop { get; set; }
        public double TargetCenterX { get; set; }
        public double TargetCenterY { get; set; }
        public string? MatchedLabelItemName { get; set; }
        public string? MatchedLabelText { get; set; }
        public string? MatchedLabelType { get; set; }
        public double? Distance { get; set; }
        public double? DeltaX { get; set; }
        public double? DeltaY { get; set; }
        public bool DuplicateLabelMatch { get; set; }
        public int CandidateCount { get; set; }
        public int SkippedCandidateCount { get; set; }
        public List<HmiLabelCandidate> Candidates { get; set; } = new List<HmiLabelCandidate>();
    }

    public sealed class HmiLabelCandidate
    {
        public string LabelItemName { get; set; } = string.Empty;
        public string LabelType { get; set; } = string.Empty;
        public string? LabelText { get; set; }
        public double LabelCenterX { get; set; }
        public double LabelCenterY { get; set; }
        public double Distance { get; set; }
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }

        internal static HmiLabelCandidate FromValues(
            double targetCenterX,
            double targetCenterY,
            string labelName,
            string labelType,
            string? labelText,
            double labelCenterX,
            double labelCenterY)
        {
            var deltaX = labelCenterX - targetCenterX;
            var deltaY = labelCenterY - targetCenterY;
            return new HmiLabelCandidate
            {
                LabelItemName = labelName,
                LabelType = labelType,
                LabelText = labelText,
                LabelCenterX = labelCenterX,
                LabelCenterY = labelCenterY,
                DeltaX = deltaX,
                DeltaY = deltaY,
                Distance = Math.Round(Math.Sqrt(deltaX * deltaX + deltaY * deltaY), 3)
            };
        }
    }
}
