using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Contracts.Compatibility;
using OpenTiaToolkit.Contracts.Protocol;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Worker.V21;

internal sealed class ApiSelfDescriptionService
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();
    private static readonly string[] AssemblyNames =
    {
        "Siemens.Engineering.Base.dll",
        "Siemens.Engineering.Step7.dll",
        "Siemens.Engineering.WinCCUnified.dll",
        "Siemens.Engineering.Startdrive.dll",
        "Siemens.Engineering.Safety.dll",
        "Siemens.Engineering.SafetyValidation.dll"
    };
    private static readonly IReadOnlyDictionary<string, string[]> FeatureTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["unified-alarms"] = new[] { "Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmClass", "Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarm" },
        ["unified-runtime-settings"] = new[] { "Siemens.Engineering.HmiUnified.HmiSoftware" },
        ["unified-text-lists"] = new[] { "Siemens.Engineering.HmiUnified.TextGraphicList.HmiTextList" },
        ["startdrive-telegrams"] = new[] { "Siemens.Engineering.MC.Drives.DriveObject", "Siemens.Engineering.MC.Drives.Telegram" },
        ["safety-administration"] = new[] { "Siemens.Engineering.Safety.SafetyAdministration" },
        ["multiuser"] = new[] { "Siemens.Engineering.Multiuser.MultiuserProject" },
        ["project-security"] = new[] { "Siemens.Engineering.Security.LocalCertificateManager", "Siemens.Engineering.Security.SyslogServerProvider" },
        ["plc-watch-tables"] = new[] { "Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable" },
        ["plc-technology-objects"] = new[] { "Siemens.Engineering.SW.TechnologicalObjects.TechnologicalInstanceDB" },
        ["plc-alarm-text-lists"] = new[] { "Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmTextlist" }
    };

    public ApiSelfDescription Describe()
    {
        string root = Environment.GetEnvironmentVariable("OPEN_TIA_PORTAL_ROOT") ?? @"C:\Program Files\Siemens\Automation\Portal V21";
        string api = Path.Combine(root, "PublicAPI", "V21", "net48");
        var assemblies = new List<Assembly>();
        var surfaces = new List<ApiAssemblySurface>();
        foreach (string name in AssemblyNames)
        {
            string path = Path.Combine(api, name);
            if (!File.Exists(path)) continue;
            Assembly assembly = Assembly.LoadFrom(path);
            assemblies.Add(assembly);
            Type[] types = SafeExportedTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
            string[] signatures = types.SelectMany(PublicSignatures).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            surfaces.Add(new ApiAssemblySurface
            {
                Name = name,
                Version = assembly.GetName().Version?.ToString() ?? string.Empty,
                Sha256 = FileSha256(path),
                PublicTypeCount = types.Length,
                PublicMemberCount = signatures.Length,
                PublicSurfaceSha256 = Sha256(string.Join("\n", signatures))
            });
        }
        var probes = FeatureTypes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Any(typeName => assemblies.Any(assembly => assembly.GetType(typeName, false, false) != null)),
            StringComparer.OrdinalIgnoreCase);
        probes["unified-discrete-alarm-write"] = HasUnifiedDiscreteAlarmWriteSurface(assemblies);
        string aggregate = Sha256(string.Join("\n", surfaces.OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name + "|" + item.Version + "|" + item.PublicSurfaceSha256)));
        return new ApiSelfDescription
        {
            TiaVersion = "V21",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Assemblies = surfaces,
            FeatureProbes = probes,
            AggregateSurfaceSha256 = aggregate
        };
    }

    public ApiMigrationReport Diff(string baselinePath)
    {
        string fullPath = Path.GetFullPath(baselinePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("API baseline profile was not found.", fullPath);
        ApiSelfDescription baseline = JsonSerializer.Deserialize<ApiSelfDescription>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidDataException("API baseline profile was empty.");
        ApiSelfDescription current = Describe();
        var differences = new List<ApiMigrationDifference>();
        foreach (ApiAssemblySurface expected in baseline.Assemblies)
        {
            ApiAssemblySurface? actual = current.Assemblies.FirstOrDefault(item => string.Equals(item.Name, expected.Name, StringComparison.OrdinalIgnoreCase));
            if (actual == null)
            {
                differences.Add(Difference("assemblies/" + expected.Name, expected.PublicSurfaceSha256, null, "breaking"));
                continue;
            }
            if (!string.Equals(expected.PublicSurfaceSha256, actual.PublicSurfaceSha256, StringComparison.OrdinalIgnoreCase))
                differences.Add(Difference("assemblies/" + expected.Name + "/publicSurfaceSha256", expected.PublicSurfaceSha256, actual.PublicSurfaceSha256, "breaking"));
            if (!string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
                differences.Add(Difference("assemblies/" + expected.Name + "/binarySha256", expected.Sha256, actual.Sha256, "informational"));
        }
        foreach (KeyValuePair<string, bool> expected in baseline.FeatureProbes)
        {
            bool actual = current.FeatureProbes.TryGetValue(expected.Key, out bool value) && value;
            if (expected.Value != actual)
                differences.Add(Difference("features/" + expected.Key, expected.Value.ToString(), actual.ToString(), expected.Value ? "breaking" : "additive"));
        }
        return new ApiMigrationReport
        {
            Compatible = differences.All(item => !string.Equals(item.Severity, "breaking", StringComparison.OrdinalIgnoreCase)),
            BaselinePath = fullPath,
            CurrentAggregateSurfaceSha256 = current.AggregateSurfaceSha256,
            Differences = differences
        };
    }

    public static string DefaultBaselinePath() => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "compatibility", "tia-v21.api.json");

    private static ApiMigrationDifference Difference(string path, string? baseline, string? current, string severity) =>
        new ApiMigrationDifference { Path = path, Baseline = baseline, Current = current, Severity = severity };

    private static IEnumerable<string> PublicSignatures(Type type)
    {
        yield return "T|" + type.FullName;
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            yield return member.MemberType + "|" + type.FullName + "|" + member;
    }

    private static Type[] SafeExportedTypes(Assembly assembly)
    {
        try { return assembly.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null && type!.IsPublic).Cast<Type>().ToArray(); }
    }

    private static bool HasUnifiedDiscreteAlarmWriteSurface(IEnumerable<Assembly> assemblies)
    {
        Type? composition = assemblies.Select(assembly => assembly.GetType("Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarmComposition", false, false)).FirstOrDefault(type => type != null);
        Type? alarm = assemblies.Select(assembly => assembly.GetType("Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarm", false, false)).FirstOrDefault(type => type != null);
        return composition?.GetMethod("Create", new[] { typeof(string) }) != null
            && alarm?.GetMethod("Delete", Type.EmptyTypes) != null
            && alarm.GetMethod("Validate", Type.EmptyTypes) != null;
    }

    private static string FileSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string Sha256(string value)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }
}

internal sealed class DescribeApiCommand : ICommand
{
    private readonly ApiSelfDescriptionService _service;
    public DescribeApiCommand(ApiSelfDescriptionService service) { _service = service; }
    public string Name => "describe-api";
    public string Description => "Self-describes the installed V21 public API surface and prioritized feature availability.";
    public CommandSchema Schema => new CommandSchema(Description);
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_service.Describe()));
}

internal sealed class DiffApiProfileCommand : ICommand
{
    private readonly ApiSelfDescriptionService _service;
    public DiffApiProfileCommand(ApiSelfDescriptionService service) { _service = service; }
    public string Name => "diff-api-profile";
    public string Description => "Compares the installed V21 public API self-description with a committed migration baseline.";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("baseline-path", CommandParameterType.Path, "Optional API profile baseline", false) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        ApiMigrationReport report = _service.Diff(args.GetString("baseline-path", ApiSelfDescriptionService.DefaultBaselinePath()));
        return Task.FromResult(report.Compatible
            ? CommandResult.CreateSuccess(report, "The installed API is compatible with the baseline.")
            : CommandResult.CreateFailure("The installed API has breaking differences from the baseline.", report));
    }
}
