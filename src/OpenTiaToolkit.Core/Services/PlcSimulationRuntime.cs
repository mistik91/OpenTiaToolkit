using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcSimulationRuntime : IPlcSimulationRuntime
{
    private const string ManagerTypeName = "Siemens.Simatic.Simulation.Runtime.SimulationRuntimeManager";
    private readonly string? _apiPath;

    public PlcSimulationRuntime() : this(null) { }
    internal PlcSimulationRuntime(string? apiPath) => _apiPath = apiPath;

    public PlcSimulatorCapabilityDiscovery Discover(string plcName, string? instanceName = null, bool refreshTags = false)
    {
        var result = new PlcSimulatorCapabilityDiscovery
        {
            ObservedAtUtc = DateTimeOffset.UtcNow,
            RequestedPlcName = plcName ?? string.Empty,
            RequestedInstanceName = instanceName ?? string.Empty
        };
        object? manager = null;
        try
        {
            string api = ResolveApiPath();
            Assembly assembly = Assembly.LoadFrom(api);
            Type managerType = assembly.GetType(ManagerTypeName, throwOnError: true)!;
            manager = Activator.CreateInstance(managerType)!;
            result.ApiPath = api;
            result.ApiVersion = FormatVersion(managerType.GetProperty("Version")!.GetValue(manager));
            if (!(bool)managerType.GetProperty("IsInitialized")!.GetValue(manager)!) throw new InvalidOperationException("PLCSIM Advanced Runtime API did not initialize.");
            if (!(bool)managerType.GetProperty("IsRuntimeManagerAvailable")!.GetValue(manager)!) throw new InvalidOperationException("PLCSIM Advanced Runtime Manager is not available.");
            var instances = new List<PlcSimulatorInstanceCapability>();
            foreach (object info in (IEnumerable)managerType.GetProperty("RegisteredInstanceInfo")!.GetValue(manager)!)
            {
                int id = Convert.ToInt32(Property(info, "ID"), CultureInfo.InvariantCulture);
                string name = Convert.ToString(Property(info, "Name"), CultureInfo.InvariantCulture) ?? string.Empty;
                object instance = managerType.GetMethod("CreateInterface", new[] { typeof(int) })!.Invoke(manager, new object[] { id })!;
                instances.Add(ReadInstance(instance, refreshTags));
            }
            result.Instances = instances;
            PlcSimulatorInstanceCapability[] selected = !string.IsNullOrWhiteSpace(instanceName)
                ? instances.Where(item => item.Name.Equals(instanceName, StringComparison.OrdinalIgnoreCase)).ToArray()
                : instances.Where(item => item.ControllerName.Equals(plcName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length == 0 && string.IsNullOrWhiteSpace(instanceName) && instances.Count == 1)
            {
                selected = new[] { instances[0] };
                result.SelectionReason = "The only registered local simulator instance was selected; its downloaded controller identity must still match after probe download.";
            }
            else if (selected.Length == 1) result.SelectionReason = string.IsNullOrWhiteSpace(instanceName) ? "Matched the downloaded controller name to the selected TIA PLC." : "Matched the explicitly requested simulator instance name.";
            else if (selected.Length > 1) result.Failure = "Simulator selection is ambiguous. Supply instance-name explicitly.";
            else result.Failure = string.IsNullOrWhiteSpace(instanceName) ? "No simulator instance matched the selected PLC and more than one/zero instance was registered." : "The requested simulator instance was not found.";
            result.SelectedInstance = selected.Length == 1 ? selected[0] : null;
            result.Available = result.SelectedInstance != null;
        }
        catch (Exception ex)
        {
            result.Failure = Message(ex);
            result.Available = false;
        }
        finally { Shutdown(manager); }
        return result;
    }

    public PlcBehaviorExecutionResult Execute(PlcBehaviorVector vector, PlcSimulatorCapabilityDiscovery capability, IReadOnlyList<string> allowedTagOwners)
    {
        var result = new PlcBehaviorExecutionResult();
        object? manager = null;
        object? instance = null;
        bool restoreStop = false;
        try
        {
            if (!capability.Available || capability.SelectedInstance == null) throw new InvalidOperationException(capability.Failure.Length == 0 ? "No simulator instance was selected." : capability.Failure);
            manager = CreateManager(capability.ApiPath);
            Type managerType = manager.GetType();
            instance = managerType.GetMethod("CreateInterface", new[] { typeof(int) })!.Invoke(manager, new object[] { capability.SelectedInstance.Id })!;
            PlcSimulatorInstanceCapability current = ReadInstance(instance, refreshTags: true);
            if (!FixedEquals(current.Fingerprint, capability.SelectedInstance.Fingerprint) && !SameStableIdentity(current, capability.SelectedInstance))
                throw new InvalidOperationException("The simulator identity changed after capability discovery.");
            EnsureTags(instance, vector, allowedTagOwners);
            string initialState = Convert.ToString(instance.GetType().GetProperty("OperatingState")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!initialState.Equals("Run", StringComparison.OrdinalIgnoreCase))
            {
                instance.GetType().GetMethod("Run", Type.EmptyTypes)!.Invoke(instance, null);
                restoreStop = initialState.Equals("Stop", StringComparison.OrdinalIgnoreCase);
            }
            ApplyWrites(instance, vector.Reset);
            result.ResetApplied = true;
            var steps = new List<PlcBehaviorStepResult>();
            foreach (PlcBehaviorStep step in vector.Steps)
            {
                ApplyWrites(instance, step.Writes);
                if (step.SettleMilliseconds > 0) Thread.Sleep(step.SettleMilliseconds);
                var assertions = step.Assertions.Select(assertion => Evaluate(instance, assertion)).ToArray();
                var stepResult = new PlcBehaviorStepResult { Name = step.Name, WrittenTags = step.Writes.Select(item => item.Tag).ToArray(), Assertions = assertions, Passed = assertions.All(item => item.Passed) };
                steps.Add(stepResult);
                if (!stepResult.Passed) break;
            }
            result.Steps = steps;
            result.Passed = steps.Count == vector.Steps.Count && steps.All(item => item.Passed);
            if (!result.Passed) result.Failure = steps.SelectMany(item => item.Assertions).FirstOrDefault(item => !item.Passed)?.Failure ?? "Behavior vector did not complete.";
        }
        catch (Exception ex) { result.Failure = Message(ex); result.Passed = false; }
        finally
        {
            if (instance != null)
            {
                try { ApplyWrites(instance, vector.Cleanup); result.CleanupApplied = true; }
                catch (Exception ex) { result.CleanupApplied = false; result.Passed = false; result.Failure = Append(result.Failure, "Stimulus cleanup failed: " + Message(ex)); }
                if (restoreStop) try { instance.GetType().GetMethod("Stop", Type.EmptyTypes)!.Invoke(instance, null); } catch { result.Passed = false; result.Failure = Append(result.Failure, "The simulator CPU state could not be restored to Stop."); }
            }
            Shutdown(manager);
        }
        return result;
    }

    public bool AreTagsAbsent(PlcSimulatorCapabilityDiscovery capability, IReadOnlyList<string> tagOwners, out string failure)
    {
        object? manager = null;
        try
        {
            if (capability.SelectedInstance == null) throw new InvalidOperationException("No simulator instance was selected.");
            manager = CreateManager(capability.ApiPath);
            object instance = manager.GetType().GetMethod("CreateInterface", new[] { typeof(int) })!.Invoke(manager, new object[] { capability.SelectedInstance.Id })!;
            instance.GetType().GetMethod("UpdateTagList", Type.EmptyTypes)!.Invoke(instance, null);
            string[] remaining = Tags(instance).Where(tag => tagOwners.Any(owner => PlcBehaviorVectorService.IsOwnedTag(tag.Name, owner))).Select(tag => tag.Name).Take(10).ToArray();
            if (remaining.Length > 0) { failure = "Disposable simulator tags remain after cleanup download: " + string.Join(", ", remaining); return false; }
            failure = string.Empty;
            return true;
        }
        catch (Exception ex) { failure = Message(ex); return false; }
        finally { Shutdown(manager); }
    }

    private object CreateManager(string apiPath)
    {
        string path = string.IsNullOrWhiteSpace(apiPath) ? ResolveApiPath() : apiPath;
        Type type = Assembly.LoadFrom(path).GetType(ManagerTypeName, true)!;
        object manager = Activator.CreateInstance(type)!;
        if (!(bool)type.GetProperty("IsInitialized")!.GetValue(manager)! || !(bool)type.GetProperty("IsRuntimeManagerAvailable")!.GetValue(manager)!)
        { Shutdown(manager); throw new InvalidOperationException("PLCSIM Advanced Runtime Manager is unavailable."); }
        return manager;
    }

    private static PlcSimulatorInstanceCapability ReadInstance(object instance, bool refreshTags)
    {
        Type type = instance.GetType();
        if (refreshTags) type.GetMethod("UpdateTagList", Type.EmptyTypes)!.Invoke(instance, null);
        var item = new PlcSimulatorInstanceCapability
        {
            Id = Convert.ToInt32(type.GetProperty("ID")!.GetValue(instance), CultureInfo.InvariantCulture),
            Name = Convert.ToString(type.GetProperty("Name")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty,
            ControllerName = Convert.ToString(type.GetProperty("ControllerName")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty,
            CpuType = Convert.ToString(type.GetProperty("CPUType")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty,
            ArticleNumber = Convert.ToString(type.GetProperty("ArticleNumber")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty,
            OperatingState = Convert.ToString(type.GetProperty("OperatingState")!.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty,
            ControllerAddresses = ((IEnumerable)type.GetProperty("ControllerIP")!.GetValue(instance)!).Cast<object>().Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).ToArray(),
            TagCount = refreshTags ? Tags(instance).Count : 0
        };
        item.Fingerprint = Hash(string.Join("|", item.Id, item.Name, item.ControllerName, item.CpuType, item.ArticleNumber, string.Join(",", item.ControllerAddresses)));
        return item;
    }

    private static void EnsureTags(object instance, PlcBehaviorVector vector, IReadOnlyList<string> owners)
    {
        var tags = Tags(instance).ToDictionary(item => Normalize(item.Name), StringComparer.OrdinalIgnoreCase);
        IEnumerable<(string Tag, string Type)> referenced = vector.Reset.Select(item => (item.Tag, item.Type))
            .Concat(vector.Steps.SelectMany(step => step.Writes.Select(item => (item.Tag, item.Type)).Concat(step.Assertions.Select(item => (item.Tag, item.Type)))))
            .Concat(vector.Cleanup.Select(item => (item.Tag, item.Type)));
        foreach ((string tag, string requestedType) in referenced.Distinct())
        {
            if (!owners.Any(owner => PlcBehaviorVectorService.IsOwnedTag(tag, owner))) throw new InvalidOperationException("Behavior tag is outside the disposable probe: " + tag);
            if (!tags.TryGetValue(Normalize(tag), out RuntimeTag? actual)) throw new InvalidOperationException("Behavior tag was not published by the downloaded simulator program: " + tag);
            string canonical = PlcBehaviorVectorService.CanonicalType(requestedType);
            if (!actual.PrimitiveType.Equals(canonical, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Behavior tag '{tag}' is {actual.PrimitiveType}, not requested {canonical}.");
        }
    }

    private static List<RuntimeTag> Tags(object instance)
    {
        var result = new List<RuntimeTag>();
        foreach (object tag in (IEnumerable)instance.GetType().GetProperty("TagInfos")!.GetValue(instance)!)
            result.Add(new RuntimeTag(Convert.ToString(Property(tag, "Name"), CultureInfo.InvariantCulture) ?? string.Empty, Convert.ToString(Property(tag, "PrimitiveDataType"), CultureInfo.InvariantCulture) ?? string.Empty));
        return result;
    }

    private static void ApplyWrites(object instance, IReadOnlyList<PlcBehaviorWrite> writes)
    {
        Type type = instance.GetType();
        foreach (PlcBehaviorWrite write in writes)
        {
            string canonical = PlcBehaviorVectorService.CanonicalType(write.Type);
            object value = PlcBehaviorVectorService.ConvertValue(canonical, write.Value);
            MethodInfo method = type.GetMethod("Write" + canonical, new[] { typeof(string), value.GetType() }) ?? throw new MissingMethodException("PLCSIM Runtime API does not expose Write" + canonical + ".");
            method.Invoke(instance, new[] { (object)write.Tag, value });
        }
    }

    private static PlcBehaviorAssertionResult Evaluate(object instance, PlcBehaviorAssertion assertion)
    {
        string canonical = PlcBehaviorVectorService.CanonicalType(assertion.Type);
        MethodInfo method = instance.GetType().GetMethod("Read" + canonical, new[] { typeof(string) }) ?? throw new MissingMethodException("PLCSIM Runtime API does not expose Read" + canonical + ".");
        object expected = PlcBehaviorVectorService.ConvertValue(canonical, assertion.Expected);
        var observations = new List<PlcBehaviorObservation>();
        var elapsed = Stopwatch.StartNew();
        long? stableSince = null;
        while (true)
        {
            object actual = method.Invoke(instance, new object[] { assertion.Tag })!;
            bool matched = Compare(actual, expected, assertion.Operator, assertion.Tolerance);
            observations.Add(new PlcBehaviorObservation { ObservedAtUtc = DateTimeOffset.UtcNow, Tag = assertion.Tag, Type = canonical, Value = Format(actual), Matched = matched });
            if (matched)
            {
                if (!stableSince.HasValue) stableSince = elapsed.ElapsedMilliseconds;
                if (elapsed.ElapsedMilliseconds - stableSince.Value >= assertion.StableMilliseconds)
                    return new PlcBehaviorAssertionResult { Tag = assertion.Tag, Operator = assertion.Operator, Expected = Format(expected), Passed = true, ElapsedMilliseconds = elapsed.ElapsedMilliseconds, Observations = observations };
            }
            else stableSince = null;
            if (elapsed.ElapsedMilliseconds >= assertion.TimeoutMilliseconds)
                return new PlcBehaviorAssertionResult { Tag = assertion.Tag, Operator = assertion.Operator, Expected = Format(expected), Passed = false, ElapsedMilliseconds = elapsed.ElapsedMilliseconds, Observations = observations, Failure = $"Assertion timed out: {assertion.Tag} {assertion.Operator} {Format(expected)}; last value {Format(actual)}." };
            Thread.Sleep(assertion.PollMilliseconds);
        }
    }

    private static bool Compare(object actual, object expected, string op, double? tolerance)
    {
        if (actual is string || actual is char || actual is bool)
        {
            bool equal = Equals(actual, expected);
            if (op.Equals("Equal", StringComparison.OrdinalIgnoreCase)) return equal;
            if (op.Equals("NotEqual", StringComparison.OrdinalIgnoreCase)) return !equal;
            return false;
        }
        double left = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
        double right = Convert.ToDouble(expected, CultureInfo.InvariantCulture);
        double delta = Math.Abs(left - right);
        if (op.Equals("Equal", StringComparison.OrdinalIgnoreCase)) return delta <= (tolerance ?? 0);
        if (op.Equals("NotEqual", StringComparison.OrdinalIgnoreCase)) return delta > (tolerance ?? 0);
        if (op.Equals("GreaterThan", StringComparison.OrdinalIgnoreCase)) return left > right;
        if (op.Equals("GreaterThanOrEqual", StringComparison.OrdinalIgnoreCase)) return left >= right;
        if (op.Equals("LessThan", StringComparison.OrdinalIgnoreCase)) return left < right;
        if (op.Equals("LessThanOrEqual", StringComparison.OrdinalIgnoreCase)) return left <= right;
        return false;
    }

    private string ResolveApiPath()
    {
        if (!string.IsNullOrWhiteSpace(_apiPath)) { if (!File.Exists(_apiPath)) throw new FileNotFoundException("Configured PLCSIM Runtime API was not found.", _apiPath); return Path.GetFullPath(_apiPath); }
        string? overridden = Environment.GetEnvironmentVariable("OPEN_TIA_PLCSIM_RUNTIME_API");
        if (!string.IsNullOrWhiteSpace(overridden)) { if (!File.Exists(overridden)) throw new FileNotFoundException("OPEN_TIA_PLCSIM_RUNTIME_API was not found.", overridden); return Path.GetFullPath(overridden); }
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "Siemens", "PLCSIMADV", "API");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("PLCSIM Advanced Runtime API is not installed.");
        string? path = Directory.GetDirectories(root).Select(directory => new { Directory = directory, Version = ParseVersion(Path.GetFileName(directory)) }).Where(item => item.Version != null).OrderByDescending(item => item.Version).Select(item => Path.Combine(item.Directory, "Siemens.Simatic.Simulation.Runtime.Api.x64.dll")).FirstOrDefault(File.Exists);
        return path ?? throw new FileNotFoundException("No supported x64 PLCSIM Advanced Runtime API assembly was found under " + root + ".");
    }

    private static object? Property(object value, string name)
    {
        Type type = value.GetType();
        PropertyInfo? property = type.GetProperty(name);
        if (property != null) return property.GetValue(value);
        FieldInfo? field = type.GetField(name);
        if (field != null) return field.GetValue(value);
        throw new MissingMemberException(type.FullName, name);
    }
    private static string Normalize(string value) => string.Join(".", (value ?? string.Empty).Split('.').Select(segment => segment.Trim().Trim('"')));
    private static string Format(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static string FormatVersion(object? value) { uint raw = Convert.ToUInt32(value, CultureInfo.InvariantCulture); return $"{raw >> 16}.{raw & 0xffff}"; }
    private static Version? ParseVersion(string value) { Version parsed; return Version.TryParse(value, out parsed) ? parsed : null; }
    private static string Hash(string value) { using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2", CultureInfo.InvariantCulture))); }
    private static bool FixedEquals(string left, string right) { byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty), b = Encoding.UTF8.GetBytes(right ?? string.Empty); if (a.Length != b.Length) return false; int value = 0; for (int i = 0; i < a.Length; i++) value |= a[i] ^ b[i]; return value == 0; }
    private static bool SameStableIdentity(PlcSimulatorInstanceCapability left, PlcSimulatorInstanceCapability right) => left.Id == right.Id && left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase) && left.ArticleNumber.Equals(right.ArticleNumber, StringComparison.OrdinalIgnoreCase);
    private static string Message(Exception ex) { Exception current = ex; while (current is TargetInvocationException && current.InnerException != null) current = current.InnerException; return current.Message; }
    private static string Append(string current, string addition) => string.IsNullOrWhiteSpace(current) ? addition : current + " " + addition;
    private static void Shutdown(object? manager) { if (manager == null) return; try { manager.GetType().GetMethod("Shutdown", Type.EmptyTypes)?.Invoke(manager, null); } catch { } }

    private sealed class RuntimeTag
    {
        public RuntimeTag(string name, string primitiveType) { Name = name; PrimitiveType = primitiveType; }
        public string Name { get; }
        public string PrimitiveType { get; }
    }
}
