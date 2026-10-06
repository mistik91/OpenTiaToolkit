using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;

namespace OpenTiaToolkit.Core.Services;

/// <summary>Typed V21 boundary for discrete-alarm reads and writes.</summary>
internal sealed class V21HmiDiscreteAlarmAccess : IHmiDiscreteAlarmAccess
{
    private static readonly string[] TextPropertyNames =
    {
        "EventText", "EventText1", "EventText2", "EventText3", "EventText4", "EventText5",
        "EventText6", "EventText7", "EventText8", "EventText9", "InfoText"
    };

    private readonly EngineeringSessionContext _session;
    private readonly V21UnifiedHmiAdapter _adapter;

    internal V21HmiDiscreteAlarmAccess(EngineeringSessionContext session, V21UnifiedHmiAdapter adapter)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    private HmiSoftware Software => _session.HmiSoftware
        ?? throw new InvalidOperationException("No Unified HMI is currently open.");

    public bool IsAvailable => _session.HmiSoftware != null;
    public bool IsReadOnly => Software.DiscreteAlarms.IsReadOnly;

    public IReadOnlyList<HmiDiscreteAlarmSnapshot> List(bool includeValidation) =>
        Software.DiscreteAlarms.Select(alarm => Map(alarm, includeValidation)).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

    public HmiDiscreteAlarmSnapshot? FindExact(string name, bool includeValidation)
    {
        var alarm = Software.DiscreteAlarms.Find(name);
        return alarm == null || !string.Equals(alarm.Name, name, StringComparison.Ordinal)
            ? null
            : Map(alarm, includeValidation);
    }

    public bool AlarmClassExists(string name) => Software.AlarmClasses.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal));

    public bool TagExists(string name) => _adapter.TryGetTag(Software, name, out var tag, out _) && tag != null;

    public bool CultureExists(string cultureName) => _session.Project != null &&
        _session.Project.LanguageSettings.Languages.Find(CultureInfo.GetCultureInfo(cultureName)) != null;

    public bool IdExists(uint id, string? exceptName = null) => Software.DiscreteAlarms.Any(x =>
        x.Id == id && !string.Equals(x.Name, exceptName, StringComparison.Ordinal));

    public HmiDiscreteAlarmSnapshot CreateAndApply(HmiDiscreteAlarmChangeRequest request)
    {
        var alarm = Software.DiscreteAlarms.Create(request.Name);
        SetProperties(alarm, request, creating: true);
        return Map(alarm, includeValidation: true);
    }

    public HmiDiscreteAlarmSnapshot Apply(string name, HmiDiscreteAlarmChangeRequest request)
    {
        var alarm = FindAlarmExact(name) ?? throw new InvalidOperationException($"Discrete alarm '{name}' was not found.");
        SetProperties(alarm, request, creating: false);
        return Map(alarm, includeValidation: true);
    }

    public void DeleteExact(string name)
    {
        var alarm = FindAlarmExact(name) ?? throw new InvalidOperationException($"Discrete alarm '{name}' was not found.");
        alarm.Delete();
    }

    public void ExecuteTransaction(string description, bool commit, Action action)
    {
        if (_session.Portal == null || _session.Project == null)
            throw new InvalidOperationException("No TIA Portal project is selected.");

        using (var exclusive = _session.Portal.ExclusiveAccess(description))
        using (var transaction = exclusive.Transaction(_session.Project, description))
        {
            action();
            if (commit) transaction.CommitOnDispose();
        }
    }

    public IReadOnlyList<HmiDiscreteAlarmCrossReference> GetCrossReferences(string name, out string? limitation)
    {
        limitation = null;
        var alarm = FindAlarmExact(name);
        if (alarm == null) return Array.Empty<HmiDiscreteAlarmCrossReference>();
        try
        {
            var service = alarm.GetService<CrossReferenceService>();
            if (service == null)
            {
                limitation = "The Siemens cross-reference service is not available for this alarm.";
                return Array.Empty<HmiDiscreteAlarmCrossReference>();
            }

            var serviceType = service.GetType();
            var filterType = serviceType.Assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", false, false)
                ?? typeof(CrossReferenceService).Assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", false, false);
            var method = filterType == null ? null : serviceType.GetMethod("GetCrossReferences", new[] { filterType });
            if (method == null || filterType == null)
            {
                limitation = "The installed Siemens cross-reference query signature is not supported.";
                return Array.Empty<HmiDiscreteAlarmCrossReference>();
            }

            var raw = method.Invoke(service, new[] { Enum.Parse(filterType, "AllObjects") });
            return Flatten(raw).Select(MapCrossReference).ToArray();
        }
        catch (Exception ex)
        {
            limitation = "Cross-reference enumeration failed: " + Unwrap(ex).Message;
            return Array.Empty<HmiDiscreteAlarmCrossReference>();
        }
    }

    private HmiDiscreteAlarm? FindAlarmExact(string name)
    {
        var alarm = Software.DiscreteAlarms.Find(name);
        return alarm != null && string.Equals(alarm.Name, name, StringComparison.Ordinal) ? alarm : null;
    }

    private static void SetProperties(HmiDiscreteAlarm alarm, HmiDiscreteAlarmChangeRequest request, bool creating)
    {
        if (request.PresentProperties.Contains("id")) alarm.Id = Require(request.Id, "id");
        if (request.PresentProperties.Contains("alarmClass")) alarm.AlarmClass = RequireString(request.AlarmClass, "alarmClass");
        if (request.PresentProperties.Contains("priority")) alarm.Priority = Require(request.Priority, "priority");
        if (request.PresentProperties.Contains("origin")) alarm.Origin = RequireClearable(request.Origin);
        if (request.PresentProperties.Contains("area")) alarm.Area = RequireClearable(request.Area);
        if (request.PresentProperties.Contains("auditClass")) alarm.AuditClass = RequireClearable(request.AuditClass);
        if (request.PresentProperties.Contains("raisedStateTag")) alarm.RaisedStateTag = RequireString(request.RaisedStateTag, "raisedStateTag");
        if (request.PresentProperties.Contains("raisedStateTagBitNumber")) alarm.RaisedStateTagBitNumber = Require(request.RaisedStateTagBitNumber, "raisedStateTagBitNumber");
        if (request.PresentProperties.Contains("triggerMode")) alarm.TriggerMode = ParseTriggerMode(request.TriggerMode);
        if (request.PresentProperties.Contains("acknowledgmentStateTag")) alarm.AcknowledgmentStateTag = RequireClearable(request.AcknowledgmentStateTag);
        if (request.PresentProperties.Contains("acknowledgmentStateTagBitNumber")) alarm.AcknowledgmentStateTagBitNumber = Require(request.AcknowledgmentStateTagBitNumber, "acknowledgmentStateTagBitNumber");
        if (request.PresentProperties.Contains("acknowledgmentControlTag")) alarm.AcknowledgmentControlTag = RequireClearable(request.AcknowledgmentControlTag);
        if (request.PresentProperties.Contains("acknowledgmentControlTagBitNumber")) alarm.AcknowledgmentControlTagBitNumber = Require(request.AcknowledgmentControlTagBitNumber, "acknowledgmentControlTagBitNumber");
        if (request.PresentProperties.Contains("alarmParameterTags"))
            alarm.AlarmParameterTags = request.AlarmParameterTags == null ? new List<string>() : request.AlarmParameterTags.ToList();
        if (request.PresentProperties.Contains("texts")) SetTexts(alarm, request.Texts);
    }

    private static void SetTexts(HmiDiscreteAlarm alarm, IReadOnlyDictionary<string, HmiDiscreteAlarmTextPatch>? texts)
    {
        if (texts == null) throw new InvalidOperationException("texts cannot be null; use explicit text fields to clear values.");
        foreach (var culturePatch in texts)
        {
            var culture = culturePatch.Key;
            var patch = culturePatch.Value;
            foreach (var propertyName in patch.PresentProperties)
            {
                var multilingual = (MultilingualText?)typeof(HmiDiscreteAlarm).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(alarm);
                var item = multilingual?.Items.FirstOrDefault(x => string.Equals(x.Language.Culture.Name, culture, StringComparison.OrdinalIgnoreCase));
                if (item == null) throw new InvalidOperationException($"Culture '{culture}' is not configured in the Unified HMI project.");
                var logical = (string?)typeof(HmiDiscreteAlarmTextPatch).GetProperty(propertyName)?.GetValue(patch);
                item.Text = UnifiedRichTextCodec.Encode(logical ?? string.Empty);
            }
        }
    }

    private static HmiDiscreteAlarmTriggerMode ParseTriggerMode(string? value)
    {
        if (!Enum.TryParse(value, true, out HmiDiscreteAlarmTriggerMode result) ||
            !Enum.GetNames(typeof(HmiDiscreteAlarmTriggerMode)).Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Invalid triggerMode '{value}'. Expected OnRisingEdge or OnFallingEdge.");
        return result;
    }

    private static HmiDiscreteAlarmSnapshot Map(HmiDiscreteAlarm alarm, bool includeValidation)
    {
        var texts = new SortedDictionary<string, HmiDiscreteAlarmLocalizedText>(StringComparer.Ordinal);
        foreach (var propertyName in TextPropertyNames)
        {
            var multilingual = (MultilingualText?)typeof(HmiDiscreteAlarm).GetProperty(propertyName)?.GetValue(alarm);
            if (multilingual == null) continue;
            foreach (var item in multilingual.Items)
            {
                var culture = item.Language.Culture.Name;
                if (!texts.TryGetValue(culture, out var localized)) texts[culture] = localized = new HmiDiscreteAlarmLocalizedText();
                typeof(HmiDiscreteAlarmLocalizedText).GetProperty(propertyName)?.SetValue(localized, UnifiedRichTextCodec.Decode(item.Text));
            }
        }

        var diagnostics = includeValidation
            ? alarm.Validate().Select(x => new HmiDiscreteAlarmValidationDiagnostic
            {
                PropertyName = x.PropertyName ?? string.Empty,
                Errors = x.Errors?.ToArray() ?? Array.Empty<string>(),
                Warnings = x.Warnings?.ToArray() ?? Array.Empty<string>()
            }).ToArray()
            : Array.Empty<HmiDiscreteAlarmValidationDiagnostic>();

        return new HmiDiscreteAlarmSnapshot
        {
            Name = alarm.Name,
            Id = alarm.Id,
            AlarmClass = alarm.AlarmClass ?? string.Empty,
            Priority = alarm.Priority,
            Origin = alarm.Origin ?? string.Empty,
            Area = alarm.Area ?? string.Empty,
            AuditClass = alarm.AuditClass ?? string.Empty,
            RaisedStateTag = alarm.RaisedStateTag ?? string.Empty,
            RaisedStateTagBitNumber = alarm.RaisedStateTagBitNumber,
            TriggerMode = alarm.TriggerMode.ToString(),
            TriggerBitAddress = alarm.TriggerBitAddress ?? string.Empty,
            AcknowledgmentStateTag = alarm.AcknowledgmentStateTag ?? string.Empty,
            AcknowledgmentStateTagBitNumber = alarm.AcknowledgmentStateTagBitNumber,
            AcknowledgmentControlTag = alarm.AcknowledgmentControlTag ?? string.Empty,
            AcknowledgmentControlTagBitNumber = alarm.AcknowledgmentControlTagBitNumber,
            AlarmParameterTags = ToStrings(alarm.AlarmParameterTags),
            Texts = texts,
            Validation = diagnostics
        };
    }

    private static IReadOnlyList<string> ToStrings(object? value) => value is IEnumerable enumerable
        ? enumerable.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToArray()
        : Array.Empty<string>();

    private static T Require<T>(T? value, string name) where T : struct =>
        value ?? throw new InvalidOperationException($"{name} cannot be null.");

    private static string RequireString(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} cannot be null or empty.") : value!;

    private static string RequireClearable(string? value) => value ?? string.Empty;

    private static IEnumerable<object> Flatten(object? value)
    {
        if (value is not IEnumerable enumerable || value is string) yield break;
        foreach (var entry in enumerable)
        {
            if (entry == null) continue;
            var sources = GetEnumerable(entry, "Sources");
            if (sources == null) yield return entry;
            else foreach (var source in sources) yield return source;
        }
    }

    private static HmiDiscreteAlarmCrossReference MapCrossReference(object value) => new()
    {
        Name = GetString(value, "Name") ?? string.Empty,
        Path = GetString(value, "Path"),
        Type = GetString(value, "TypeName", "ItemType", "ObjectType", "Type"),
        Detail = GetString(value, "Address", "Device", "Access")
    };

    private static IEnumerable? GetEnumerable(object value, string property) => value.GetType().GetProperty(property)?.GetValue(value) as IEnumerable;
    private static string? GetString(object value, params string[] properties) => properties.Select(x => value.GetType().GetProperty(x)?.GetValue(value)?.ToString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
}
