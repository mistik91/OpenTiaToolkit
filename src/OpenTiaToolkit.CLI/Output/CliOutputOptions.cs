using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.CLI.Output
{
    /// <summary>
    /// CLI-wide output settings parsed before command routing.
    /// </summary>
    public sealed class CliOutputOptions
    {
        public CliOutputOptions(CliOutputFormat format, string[] commandArgs)
        {
            Format = format;
            CommandArgs = commandArgs;
        }

        public CliOutputFormat Format { get; }

        public string[] CommandArgs { get; }

        public static CliOutputOptions Default(string[] commandArgs)
        {
            return new CliOutputOptions(CliOutputFormat.Table, commandArgs);
        }

        public static bool TryParse(string[] args, out CliOutputOptions options, out string? error)
        {
            var commandArgs = new List<string>();
            var format = CliOutputFormat.Table;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("--format=".Length);
                    if (!TryParseFormat(value, out format))
                    {
                        error = InvalidFormatMessage(value);
                        options = Default(args);
                        return false;
                    }

                    continue;
                }

                if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-"))
                    {
                        error = "Missing value for global option: --format. Supported values: table, json.";
                        options = Default(args);
                        return false;
                    }

                    var value = args[++i];
                    if (!TryParseFormat(value, out format))
                    {
                        error = InvalidFormatMessage(value);
                        options = Default(args);
                        return false;
                    }

                    continue;
                }

                commandArgs.Add(arg);
            }

            options = new CliOutputOptions(format, commandArgs.ToArray());
            return true;
        }

        private static bool TryParseFormat(string value, out CliOutputFormat format)
        {
            if (string.Equals(value, "table", StringComparison.OrdinalIgnoreCase))
            {
                format = CliOutputFormat.Table;
                return true;
            }

            if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
            {
                format = CliOutputFormat.Json;
                return true;
            }

            format = CliOutputFormat.Table;
            return false;
        }

        private static string InvalidFormatMessage(string value)
        {
            return $"Invalid value for global option --format: '{value}'. Supported values: table, json.";
        }
    }

    public enum CliOutputFormat
    {
        Table,
        Json
    }

    public static class CliOutputFormatter
    {
        public static string Format(CommandOutcome result, CliOutputFormat format)
        {
            return format == CliOutputFormat.Json
                ? JsonSerializer.Serialize(result, ContractJson.CreateOptions(writeIndented: true))
                : FormatText(result);
        }

        private static string FormatText(CommandOutcome result)
        {
            if (!result.Success)
            {
                return $"Error [{result.Error?.Code ?? ErrorCode.InternalError}] ({result.CorrelationId}): {result.Message}";
            }

            if (!result.Data.HasValue || result.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return result.Message;
            JsonElement data = result.Data.Value;
            if (data.ValueKind == JsonValueKind.String) return data.GetString() ?? string.Empty;
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
