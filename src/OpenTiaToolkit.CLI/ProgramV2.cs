using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.CLI.Routing;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.CLI;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (!TryParseScript(args, out string? scriptPath, out string? scriptError, out string[] remaining))
        {
            Console.Error.WriteLine(scriptError);
            return 2;
        }

        if (!CliOutputOptions.TryParse(remaining, out CliOutputOptions output, out string? formatError))
        {
            Console.Error.WriteLine(formatError);
            return 2;
        }

        try
        {
            string worker = ToolkitSession.ResolveWorkerPath(typeof(Program).Assembly.Location);
            using (ToolkitSession session = await ToolkitSession.StartAsync(
                worker, ToolkitSession.ResolveDefaultAuditPath(), CancellationToken.None).ConfigureAwait(false))
            {
                var router = new RemoteCommandRouter(session);
                if (scriptPath != null) return await RunScriptAsync(scriptPath, router, output.Format).ConfigureAwait(false);
                if (output.CommandArgs.Length == 0) return await RunReplAsync(router).ConfigureAwait(false);
                return await RunOneAsync(output.CommandArgs, router, output.Format).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + (ex.InnerException?.Message ?? ex.Message));
            return 1;
        }
    }

    private static async Task<int> RunOneAsync(string[] args, RemoteCommandRouter router, CliOutputFormat format)
    {
        if (args.Length == 1 && string.Equals(args[0], "--command-catalog", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(router.GetCommandCatalog(), new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (args.Length > 0 && (args[0] == "help" || args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine(router.GetHelp(args.Length > 1 ? args[1] : null));
            return 0;
        }

        if (args.Length > 1 && (args[1] == "--help" || args[1] == "-h"))
        {
            Console.WriteLine(router.GetHelp(args[0]));
            return 0;
        }

        CommandOutcome result = await router.ExecuteAsync(args).ConfigureAwait(false);
        Console.WriteLine(CliOutputFormatter.Format(result, format));
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunScriptAsync(string path, RemoteCommandRouter router, CliOutputFormat format)
    {
        TextReader reader = path == "-" ? Console.In : new StreamReader(Path.GetFullPath(path), Encoding.UTF8);
        try
        {
            string? line;
            int lineNumber = 0;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lineNumber++;
                string trimmed = NormalizeScriptLine(line).Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                if (string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase)) return 0;
                if (!CommandLineTokenizer.TryTokenize(trimmed, out string[] commandArgs, out string? error))
                {
                    Console.Error.WriteLine($"Script stopped at line {lineNumber}: {error}");
                    return 2;
                }

                int exit = await RunOneAsync(commandArgs, router, format).ConfigureAwait(false);
                if (exit != 0)
                {
                    Console.Error.WriteLine($"Script stopped at line {lineNumber}.");
                    return exit;
                }
            }

            return 0;
        }
        finally
        {
            if (path != "-") reader.Dispose();
        }
    }

    private static string NormalizeScriptLine(string line)
    {
        string normalized = line.TrimStart('\uFEFF');
        const string utf8BomDecodedAsLatin1 = "\u00EF\u00BB\u00BF";
        const string utf8BomDecodedAsOem850 = "\u00B4\u2557\u2510";
        if (normalized.StartsWith(utf8BomDecodedAsLatin1, StringComparison.Ordinal))
            return normalized.Substring(utf8BomDecodedAsLatin1.Length);
        return normalized.StartsWith(utf8BomDecodedAsOem850, StringComparison.Ordinal)
            ? normalized.Substring(utf8BomDecodedAsOem850.Length)
            : normalized;
    }

    private static async Task<int> RunReplAsync(RemoteCommandRouter router)
    {
        Console.WriteLine("OpenTiaToolkit CLI - isolated worker session. Type 'help' or 'exit'.");
        while (true)
        {
            Console.Write("tia> ");
            string? line = Console.ReadLine();
            if (line == null || string.Equals(line.Trim(), "exit", StringComparison.OrdinalIgnoreCase)) return 0;
            if (!CommandLineTokenizer.TryTokenize(line, out string[] args, out string? error))
            {
                Console.Error.WriteLine(error);
                continue;
            }

            await RunOneAsync(args, router, CliOutputFormat.Table).ConfigureAwait(false);
        }
    }

    private static bool TryParseScript(string[] args, out string? path, out string? error, out string[] remaining)
    {
        path = null;
        error = null;
        var kept = new System.Collections.Generic.List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--script", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length) { error = "Missing value for global option: --script."; remaining = args; return false; }
                path = args[++index];
            }
            else kept.Add(args[index]);
        }

        remaining = kept.ToArray();
        return true;
    }
}
