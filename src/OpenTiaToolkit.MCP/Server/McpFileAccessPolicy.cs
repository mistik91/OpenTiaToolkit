using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.MCP.Server
{
    public sealed class McpFileAccessPolicy
    {
        private static readonly HashSet<string> OutputParameterNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "output-path",
                "export-path",
                "patch-path",
                "backup-path",
                "working-path",
                "destination-path"
            };

        private readonly IReadOnlyList<string> _allowedRoots;
        private readonly long _maxInputFileBytes;
        private readonly bool _allowOverwrite;

        public McpFileAccessPolicy(McpRuntimeOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _allowedRoots = (options.AllowedFileRoots.Count == 0
                    ? GetDefaultAllowedRoots()
                    : options.AllowedFileRoots)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _maxInputFileBytes = options.MaxInputFileBytes;
            _allowOverwrite = options.AllowFileOverwrite;
        }

        public string? Validate(
            ICommand command,
            IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            if (arguments == null)
            {
                return null;
            }

            foreach (var parameter in command.Schema.Parameters.Where(parameter => parameter.Type == CommandParameterType.Path))
            {
                if (!arguments.TryGetValue(parameter.Name, out var rawPath) ||
                    rawPath.ValueKind == JsonValueKind.Null ||
                    rawPath.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                if (rawPath.ValueKind != JsonValueKind.String)
                {
                    return $"MCP file policy rejected {parameter.Name}: expected a string path.";
                }

                var path = rawPath.GetString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path!);
                }
                catch (Exception ex)
                {
                    return $"MCP file policy rejected {parameter.Name}: {ex.Message}";
                }

                if (!_allowedRoots.Any(root => IsPathUnderRoot(fullPath, root)))
                {
                    return $"MCP file policy rejected {parameter.Name} '{fullPath}'. Allowed roots: {string.Join("; ", _allowedRoots)}";
                }

                var reparsePoint = FindExistingReparsePoint(fullPath);
                if (reparsePoint != null)
                {
                    return $"MCP file policy rejected {parameter.Name} because path component '{reparsePoint}' is a reparse point.";
                }

                if (File.Exists(fullPath))
                {
                    if (OutputParameterNames.Contains(parameter.Name) && !_allowOverwrite)
                    {
                        return $"MCP file policy rejected overwrite of existing file '{fullPath}'. Set OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE=true only when overwrite is intended.";
                    }

                    var length = new FileInfo(fullPath).Length;
                    if (_maxInputFileBytes > 0 && length > _maxInputFileBytes)
                    {
                        return $"MCP file policy rejected '{fullPath}' because it is {length} bytes; the configured maximum is {_maxInputFileBytes} bytes.";
                    }
                }
            }

            return null;
        }

        private static IReadOnlyList<string> GetDefaultAllowedRoots()
        {
            return new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Path.Combine(Path.GetTempPath(), "OpenTiaToolkit")
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string? FindExistingReparsePoint(string path)
        {
            var current = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(current))
            {
                return null;
            }

            var remainder = path.Substring(current.Length);
            foreach (var segment in remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    break;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return current;
                }
            }

            return null;
        }
    }
}
