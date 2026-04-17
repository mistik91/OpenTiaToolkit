using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Optional filters used by Unified HMI JSON export, diff, and import flows.
    /// </summary>
    public sealed class HmiJsonFilterOptions
    {
        public IReadOnlyList<string> Screens { get; set; } = new List<string>();

        public IReadOnlyList<string> ItemNames { get; set; } = new List<string>();

        public IReadOnlyList<string> Properties { get; set; } = new List<string>();
    }

    /// <summary>
    /// Unified HMI JSON document root used for exports and patches.
    /// </summary>
    public sealed class HmiJsonDocument
    {
        public string SchemaVersion { get; set; } = "1.0";

        public string DocumentKind { get; set; } = "Export";

        public string ExportedAtUtc { get; set; } = string.Empty;

        public string? PatchGeneratedAtUtc { get; set; }

        public string? HmiName { get; set; }

        public HmiJsonFilterOptions? Filters { get; set; }

        public HmiJsonPatchSummary? PatchSummary { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();

        public List<HmiJsonScreen> Screens { get; set; } = new List<HmiJsonScreen>();
    }

    /// <summary>
    /// Exported or patched Unified HMI screen.
    /// </summary>
    public sealed class HmiJsonScreen
    {
        public string Path { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Action { get; set; }

        public Dictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>();

        public List<HmiJsonEventHandler>? EventHandlers { get; set; }

        public List<HmiJsonPropertyEventHandler>? PropertyEventHandlers { get; set; }

        public List<HmiJsonDynamization>? Dynamizations { get; set; }

        public List<HmiJsonItem> Items { get; set; } = new List<HmiJsonItem>();
    }

    /// <summary>
    /// Exported or patched Unified HMI screen item.
    /// </summary>
    public sealed class HmiJsonItem
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string? Action { get; set; }

        public Dictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>();

        public List<HmiJsonEventHandler>? EventHandlers { get; set; }

        public List<HmiJsonPropertyEventHandler>? PropertyEventHandlers { get; set; }

        public List<HmiJsonDynamization>? Dynamizations { get; set; }
    }

    /// <summary>
    /// Script-backed event handler exported from or imported into Unified HMI.
    /// </summary>
    public sealed class HmiJsonEventHandler
    {
        public string EventType { get; set; } = string.Empty;

        public HmiJsonScriptPayload? Script { get; set; }
    }

    /// <summary>
    /// Common script payload used by Unified HMI event handlers.
    /// </summary>
    public sealed class HmiJsonScriptPayload
    {
        public bool? Async { get; set; }

        public string? GlobalDefinitionAreaScriptCode { get; set; }

        public string? ScriptCode { get; set; }
    }

    /// <summary>
    /// Script-backed property event handler exported from or imported into Unified HMI.
    /// </summary>
    public sealed class HmiJsonPropertyEventHandler
    {
        public string PropertyName { get; set; } = string.Empty;

        public string EventType { get; set; } = string.Empty;

        public HmiJsonScriptPayload? Script { get; set; }
    }

    /// <summary>
    /// Dynamization exported from or imported into Unified HMI.
    /// </summary>
    public sealed class HmiJsonDynamization
    {
        public string Property { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public HmiJsonDynamizationBinding? Binding { get; set; }

        public HmiJsonScriptDynamization? Script { get; set; }

        public HmiJsonFlashingDynamization? Flashing { get; set; }

        public HmiJsonValueConverter? ValueConverter { get; set; }
    }

    /// <summary>
    /// Canonical binding block for Unified HMI dynamizations.
    /// </summary>
    public sealed class HmiJsonDynamizationBinding
    {
        public string? Tag { get; set; }

        public bool? ReadOnly { get; set; }

        public bool? UseIndirectAddressing { get; set; }

        public string? ResourceList { get; set; }

        public string? DynamicTagName { get; set; }
    }

    /// <summary>
    /// Script-specific dynamization payload.
    /// </summary>
    public sealed class HmiJsonScriptDynamization
    {
        public bool? Async { get; set; }

        public string? GlobalDefinitionAreaScriptCode { get; set; }

        public string? ScriptCode { get; set; }

        public HmiJsonScriptTrigger? Trigger { get; set; }
    }

    /// <summary>
    /// Trigger metadata for script dynamizations.
    /// </summary>
    public sealed class HmiJsonScriptTrigger
    {
        public string? Type { get; set; }

        public string? CustomDuration { get; set; }

        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Flashing-specific dynamization payload.
    /// </summary>
    public sealed class HmiJsonFlashingDynamization
    {
        public string? Color { get; set; }

        public string? AlternateColor { get; set; }

        public string? FlashingCondition { get; set; }

        public string? FlashingRate { get; set; }
    }

    /// <summary>
    /// Supported value-converter fields for simple formula-based dynamizations.
    /// </summary>
    public sealed class HmiJsonValueConverter
    {
        public bool? IsFormulaSelected { get; set; }

        public string? Formula { get; set; }

        public HmiJsonMappingTable? MappingTable { get; set; }
    }

    /// <summary>
    /// Mapping-table payload for Unified HMI value converters.
    /// </summary>
    public sealed class HmiJsonMappingTable
    {
        public string? ConditionType { get; set; }

        public List<HmiJsonMappingTableEntry>? Entries { get; set; }
    }

    /// <summary>
    /// Mapping-table entry payload for Unified HMI value converters.
    /// </summary>
    public sealed class HmiJsonMappingTableEntry
    {
        public string? EntryType { get; set; }

        public string? Condition { get; set; }

        public string? From { get; set; }

        public string? To { get; set; }

        public string? RangeType { get; set; }

        public string? BitDynamizationType { get; set; }

        public string? Relevant { get; set; }

        public string? Value { get; set; }

        public string? AlternateValue { get; set; }

        public bool? Flashing { get; set; }

        public string? FlashingRate { get; set; }
    }

    /// <summary>
    /// Summary embedded in patch JSON documents.
    /// </summary>
    public sealed class HmiJsonPatchSummary
    {
        public int ScreenCreateCount { get; set; }

        public int ScreenUpdateCount { get; set; }

        public int ScreenDeleteCount { get; set; }

        public int ItemCreateCount { get; set; }

        public int ItemUpdateCount { get; set; }

        public int ItemDeleteCount { get; set; }

        public int ItemRecreateCount { get; set; }

        public int PropertyChangeCount { get; set; }
    }

    /// <summary>
    /// Summary of a Unified HMI JSON export.
    /// </summary>
    public sealed class HmiJsonExportResult
    {
        public string OutputPath { get; set; } = string.Empty;

        public int ScreenCount { get; set; }

        public int ItemCount { get; set; }

        public int WarningCount { get; set; }
    }

    /// <summary>
    /// Summary of a Unified HMI JSON patch generation run.
    /// </summary>
    public sealed class HmiJsonDiffResult
    {
        public string OutputPath { get; set; } = string.Empty;

        public int ScreenCreateCount { get; set; }

        public int ScreenUpdateCount { get; set; }

        public int ScreenDeleteCount { get; set; }

        public int ItemCreateCount { get; set; }

        public int ItemUpdateCount { get; set; }

        public int ItemDeleteCount { get; set; }

        public int ItemRecreateCount { get; set; }

        public int PropertyChangeCount { get; set; }

        public int WarningCount { get; set; }
    }

    /// <summary>
    /// Safety and execution options for Unified HMI JSON imports.
    /// </summary>
    public sealed class HmiJsonImportExecutionOptions
    {
        public bool FailOnWarning { get; set; }

        public bool RequireCleanPreflight { get; set; }

        public bool BackupBeforeImport { get; set; }

        public string? BackupPath { get; set; }
    }

    /// <summary>
    /// Agent-oriented single-screen JSON snapshot.
    /// </summary>
    public sealed class HmiScreenJsonSnapshotResult
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;

        public string? HmiName { get; set; }

        public string ScreenPath { get; set; } = string.Empty;

        public string BaseHash { get; set; } = string.Empty;

        public string? ExportPath { get; set; }

        public HmiJsonFilterOptions? SnapshotFilters { get; set; }

        public HmiScreenJsonRecommendedPatchArgs? RecommendedPatchArgs { get; set; }

        public HmiScreenJsonCapabilities? Capabilities { get; set; }

        public int WarningCount { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();

        public HmiJsonScreen? Screen { get; set; }
    }

    /// <summary>
    /// Agent-friendly argument echo for safely patching a screen snapshot.
    /// </summary>
    public sealed class HmiScreenJsonRecommendedPatchArgs
    {
        public string? HmiName { get; set; }

        public string ScreenPath { get; set; } = string.Empty;

        public string BaseHash { get; set; } = string.Empty;

        public IReadOnlyList<string> ItemNames { get; set; } = new List<string>();

        public IReadOnlyList<string> Properties { get; set; } = new List<string>();

        public bool DryRun { get; set; } = true;

        public bool Backup { get; set; } = true;
    }

    /// <summary>
    /// Agent-readable capabilities and operation hints for a screen JSON snapshot.
    /// </summary>
    public sealed class HmiScreenJsonCapabilities
    {
        public IReadOnlyList<string> SupportedPatchOperations { get; set; } = new List<string>();

        public IReadOnlyList<string> CompositeProperties { get; set; } = new List<string>();

        public IReadOnlyList<HmiScreenJsonOperationHint> OperationHints { get; set; } = new List<HmiScreenJsonOperationHint>();
    }

    /// <summary>
    /// Compact guidance for one patch operation family.
    /// </summary>
    public sealed class HmiScreenJsonOperationHint
    {
        public string Operation { get; set; } = string.Empty;

        public string UseWhen { get; set; } = string.Empty;

        public IReadOnlyList<string> RequiredFields { get; set; } = new List<string>();

        public IReadOnlyList<string> RecommendedProperties { get; set; } = new List<string>();
    }

    /// <summary>
    /// Agent-oriented result for a single-screen JSON edit plan or apply run.
    /// </summary>
    public sealed class HmiScreenJsonEditResult
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;

        public string? HmiName { get; set; }

        public string ScreenPath { get; set; } = string.Empty;

        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public bool SafeToApply { get; set; }

        public bool SafetyBlocked { get; set; }

        public string? SafetyMessage { get; set; }

        public HmiScreenJsonAgentGuidance? Guidance { get; set; }

        public HmiJsonFilterOptions? EffectiveFilters { get; set; }

        public string CurrentHash { get; set; } = string.Empty;

        public string? ExpectedBaseHash { get; set; }

        public string EditedHash { get; set; } = string.Empty;

        public string? BaseExportPath { get; set; }

        public string? EditedExportPath { get; set; }

        public string? PatchPath { get; set; }

        public string? BackupPath { get; set; }

        public string? VerifyExportPath { get; set; }

        public HmiJsonDiffResult? Diff { get; set; }

        public HmiJsonImportResult? Preflight { get; set; }

        public HmiJsonImportResult? Import { get; set; }

        public HmiJsonExportResult? VerifyExport { get; set; }

        public HmiJsonScreen? PatchScreen { get; set; }

        public HmiJsonScreen? VerifiedScreen { get; set; }

        public int PatchOperationCount { get; set; }

        public int PatchOperationSucceededCount { get; set; }

        public int PatchOperationFailedCount { get; set; }

        public List<HmiScreenJsonPatchOperationResult> PatchOperations { get; set; } = new List<HmiScreenJsonPatchOperationResult>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Agent-oriented operation request for editing one exported Unified HMI screen JSON object.
    /// </summary>
    public sealed class HmiScreenJsonPatchOperation
    {
        public string Op { get; set; } = string.Empty;

        public string? Item { get; set; }

        public string? ItemName { get; set; }

        public string? Source { get; set; }

        public string? SourceItem { get; set; }

        public string? Target { get; set; }

        public string? TargetItem { get; set; }

        public string? TargetName { get; set; }

        public string? ItemType { get; set; }

        public string? Type { get; set; }

        public string? EventType { get; set; }

        public string? ScriptCode { get; set; }

        public string? GlobalDefinitionAreaScriptCode { get; set; }

        public bool? Async { get; set; }

        public HmiJsonEventHandler? EventHandler { get; set; }

        public HmiJsonPropertyEventHandler? PropertyEventHandler { get; set; }

        public HmiJsonDynamization? Dynamization { get; set; }

        public string? DynamizationType { get; set; }

        public string? Tag { get; set; }

        public string? Property { get; set; }

        public object? Value { get; set; }

        public Dictionary<string, object?>? Properties { get; set; }

        public int? Left { get; set; }

        public int? Top { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public string? NewName { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Agent-oriented wrapper for JSON patch operations.
    /// </summary>
    public sealed class HmiScreenJsonPatchRequest
    {
        public List<HmiScreenJsonPatchOperation> Operations { get; set; } = new List<HmiScreenJsonPatchOperation>();
    }

    /// <summary>
    /// Result for one agent-oriented screen patch operation.
    /// </summary>
    public sealed class HmiScreenJsonPatchOperationResult
    {
        public int Index { get; set; }

        public string Op { get; set; } = string.Empty;

        public bool Succeeded { get; set; }

        public string Scope { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string? Property { get; set; }

        public string? PreviousValue { get; set; }

        public string? NewValue { get; set; }

        public string? Message { get; set; }

        public List<string> Suggestions { get; set; } = new List<string>();
    }

    /// <summary>
    /// Structured next-step guidance for agents after an edit or patch run.
    /// </summary>
    public sealed class HmiScreenJsonAgentGuidance
    {
        public string RecommendedNextAction { get; set; } = string.Empty;

        public HmiScreenJsonRecommendedPatchArgs? RecommendedNextArgs { get; set; }

        public bool ShouldReRead { get; set; }

        public bool ShouldApply { get; set; }

        public bool ShouldInspectPreflight { get; set; }

        public bool ShouldVerify { get; set; }

        public List<string> Reasons { get; set; } = new List<string>();

        public List<string> SuggestedFixes { get; set; } = new List<string>();

        public List<string> RequiredItemNames { get; set; } = new List<string>();

        public List<string> RequiredProperties { get; set; } = new List<string>();
    }

    /// <summary>
    /// Agent-oriented capabilities for a Unified HMI screen.
    /// </summary>
    public sealed class HmiScreenJsonCapabilitiesResult
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;

        public string? HmiName { get; set; }

        public string ScreenPath { get; set; } = string.Empty;

        public HmiScreenJsonSnapshotResult? Snapshot { get; set; }

        public HmiScreenJsonRecommendedPatchArgs? RecommendedPatchArgs { get; set; }

        public HmiScreenJsonCapabilities? Capabilities { get; set; }

        public IReadOnlyList<HmiItemTypeInfo> SupportedItemTypes { get; set; } = new List<HmiItemTypeInfo>();

        public IReadOnlyList<HmiScreenJsonItemCapability> Items { get; set; } = new List<HmiScreenJsonItemCapability>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Agent-readable capability details for one exported screen item.
    /// </summary>
    public sealed class HmiScreenJsonItemCapability
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public IReadOnlyList<string> ExportedProperties { get; set; } = new List<string>();

        public IReadOnlyList<HmiItemPropertyInfo> WritableProperties { get; set; } = new List<HmiItemPropertyInfo>();

        public IReadOnlyList<string> AvailableCompositeProperties { get; set; } = new List<string>();

        public HmiScreenJsonRecommendedPatchArgs? RecommendedPatchArgs { get; set; }
    }

    /// <summary>
    /// Summary of a screen or item object action during JSON import.
    /// </summary>
    public sealed class HmiJsonObjectAction
    {
        public string Scope { get; set; } = string.Empty;

        public string ScreenPath { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string? ItemType { get; set; }

        public string Action { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary of a property change during JSON import.
    /// </summary>
    public sealed class HmiJsonPropertyChange
    {
        public string Scope { get; set; } = string.Empty;

        public string ScreenPath { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string Property { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string? CurrentValue { get; set; }

        public string? DesiredValue { get; set; }
    }

    /// <summary>
    /// Summary of a failed or skipped property operation during JSON import.
    /// </summary>
    public sealed class HmiJsonPropertyIssue
    {
        public string Scope { get; set; } = string.Empty;

        public string ScreenPath { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string Property { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary of a Unified HMI JSON import or preflight run.
    /// </summary>
    public sealed class HmiJsonImportResult
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;

        public bool DryRun { get; set; }

        public string InputPath { get; set; } = string.Empty;

        public string? ReportPath { get; set; }

        public bool SafetyBlocked { get; set; }

        public string? SafetyMessage { get; set; }

        public string? BackupPath { get; set; }

        public int ScreenCount { get; set; }

        public int ScreenCreated { get; set; }

        public int ScreenDeleted { get; set; }

        public int ItemCount { get; set; }

        public int ItemCreated { get; set; }

        public int ItemDeleted { get; set; }

        public int ItemRecreated { get; set; }

        public int PropertySetCount { get; set; }

        public int PropertySkippedCount { get; set; }

        public int PropertyUnchangedCount { get; set; }

        public int PropertyFailedCount { get; set; }

        public int WarningCount { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();

        public List<HmiJsonObjectAction> ObjectActions { get; set; } = new List<HmiJsonObjectAction>();

        public List<HmiJsonPropertyChange> PropertyChanges { get; set; } = new List<HmiJsonPropertyChange>();

        public List<HmiJsonPropertyIssue> FailedProperties { get; set; } = new List<HmiJsonPropertyIssue>();

        public List<HmiJsonPropertyIssue> SkippedProperties { get; set; } = new List<HmiJsonPropertyIssue>();
    }
}
