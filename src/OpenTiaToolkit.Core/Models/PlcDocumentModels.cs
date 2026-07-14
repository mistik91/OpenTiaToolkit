using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcDocumentValidationResult
{
    public bool Valid { get; set; }
    public string ImportDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string S7dclPath { get; set; } = string.Empty;
    public string? S7resPath { get; set; }
    public string DocumentHash { get; set; } = string.Empty;
    public string? ResourceHash { get; set; }
    public string? BlockName { get; set; }
    public string? BlockType { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? BlockNumber { get; set; }
    public bool HasUtf8Bom { get; set; }
    public bool ResourceRequired { get; set; }
    public IReadOnlyList<string> ResourceReferences { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string Classification { get; set; } = "verified";
    public IReadOnlyList<PlcDocumentInstructionValidation> Instructions { get; set; } = Array.Empty<PlcDocumentInstructionValidation>();
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
}

public sealed class PlcDocumentInstructionValidation
{
    public string Instruction { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public int Line { get; set; }
    public string? Guidance { get; set; }
}

public sealed class PlcBlockIdentity
{
    public string PlcName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string? Number { get; set; }

    public override string ToString() => $"{PlcName}/{(string.IsNullOrEmpty(GroupPath) ? "<root>" : GroupPath)}/{BlockType}:{Name}";
}

public sealed class PlcDocumentImportPlan
{
    public string PlanId { get; set; } = string.Empty;
    public string ApplyToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string PlcName { get; set; } = string.Empty;
    public string ImportDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? GroupPath { get; set; }
    public string ImportOption { get; set; } = "Override";
    public string Action { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string BaselineHash { get; set; } = string.Empty;
    public BlockInfoDetail? ExistingBlock { get; set; }
    public PlcBlockIdentity Target { get; set; } = new PlcBlockIdentity();
    public PlcDocumentValidationResult Validation { get; set; } = new PlcDocumentValidationResult();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class PlcDocumentImportApplyResult
{
    public string PlanId { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public bool StateMatched { get; set; }
    public bool Verified { get; set; }
    public bool CompileSucceeded { get; set; }
    public bool RollbackAttempted { get; set; }
    public bool RolledBack { get; set; }
    public bool RestoredHashMatches { get; set; }
    public string? ExpectedRestoredHash { get; set; }
    public string? ActualRestoredHash { get; set; }
    public PlcBlockIdentity? Target { get; set; }
    public string Message { get; set; } = string.Empty;
    public BlockInfoDetail? ImportedBlock { get; set; }
    public CompilationResult? Compilation { get; set; }
    public CompilationResult? RollbackCompilation { get; set; }
}

public sealed class PlcCodeTransaction
{
    public string SchemaVersion { get; set; } = "1.3";
    public string TransactionId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string State { get; set; } = "Draft";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int? TiaProcessId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public string? CapabilityId { get; set; }
    public string? CapabilityFingerprint { get; set; }
    public string? CapabilityRoute { get; set; }
    public IReadOnlyList<PlcCodeCapabilityBinding> CapabilityBindings { get; set; } = Array.Empty<PlcCodeCapabilityBinding>();
    public string CompilePolicy { get; set; } = "NoNewErrors";
    public int? MaximumNewWarnings { get; set; }
    public string? ApplyToken { get; set; }
    public string? PlanHash { get; set; }
    public PlcCodeTransactionCompileEvidence? BaselineCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? FinalCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? RollbackCompilation { get; set; }
    public PlcCodeImpactAnalysis? ImpactAnalysis { get; set; }
    public bool ImpactAcknowledged { get; set; }
    public string? ImpactAcknowledgementFingerprint { get; set; }
    public IReadOnlyList<PlcCodeTransactionOperation> Operations { get; set; } = Array.Empty<PlcCodeTransactionOperation>();
    public IReadOnlyList<PlcCodeTransactionJournalEntry> Journal { get; set; } = Array.Empty<PlcCodeTransactionJournalEntry>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string? Failure { get; set; }
    public bool RecoveryPackageRetained { get; set; }
}

public sealed class PlcCodeImpactAnalysis
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Fingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public DateTimeOffset AnalyzedAtUtc { get; set; }
    public IReadOnlyList<PlcCodeImpactTarget> Targets { get; set; } = Array.Empty<PlcCodeImpactTarget>();
    public IReadOnlyList<PlcCodeImpactDependency> Dependencies { get; set; } = Array.Empty<PlcCodeImpactDependency>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeImpactTarget
{
    public int OperationSequence { get; set; }
    public PlcBlockIdentity Target { get; set; } = new PlcBlockIdentity();
    public string ChangeKind { get; set; } = "Unknown";
    public string Status { get; set; } = "Unknown";
    public string? BaselineSignature { get; set; }
    public string? CandidateSignature { get; set; }
    public bool PlcConsumerLookupSucceeded { get; set; }
    public string PlcConsumerLookupMessage { get; set; } = string.Empty;
    public string ConsumerFingerprint { get; set; } = string.Empty;
    public int ExcludedSelfReferenceCount { get; set; }
    public IReadOnlyList<PlcCodeImpactConsumer> Consumers { get; set; } = Array.Empty<PlcCodeImpactConsumer>();
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeImpactConsumer
{
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Usage { get; set; }
    public string? ItemType { get; set; }
    public string? Detail { get; set; }
}

public sealed class PlcCodeImpactDependency
{
    public int OperationSequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public PlcBlockIdentity? ResolvedTarget { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PlcCodeCapabilityBinding
{
    public string CapabilityId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string ObjectKind { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
}

public sealed class PlcCodeTransactionOperation
{
    public int Sequence { get; set; }
    public string Kind { get; set; } = "UpsertBlockDocuments";
    public string? SourcePath { get; set; }
    public string? SourceName { get; set; }
    public string TypeGroupPath { get; set; } = string.Empty;
    public string? BaselinePath { get; set; }
    public string? ValidationBaselineHash { get; set; }
    public string TargetMode { get; set; } = "Auto";
    public string? AuthoringBaselineDirectory { get; set; }
    public string? AuthoringBaselineFileName { get; set; }
    public PlcCodeChangeContract? ChangeContract { get; set; }
    public bool AllowTopologyChange { get; set; }
    public IReadOnlyList<string> AllowedChangePaths { get; set; } = Array.Empty<string>();
    public string ImportDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string ImportOption { get; set; } = "Override";
    public string Action { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string BaselineHash { get; set; } = string.Empty;
    public string? CanonicalResultHash { get; set; }
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
    public IReadOnlyList<int> ExplicitDependsOn { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> DependsOn { get; set; } = Array.Empty<int>();
    public int ExecutionOrder { get; set; }
    public PlcBlockIdentity? Target { get; set; }
    public IReadOnlyList<PlcCodeTransactionTargetSnapshot> TargetSnapshots { get; set; } = Array.Empty<PlcCodeTransactionTargetSnapshot>();
    public PlcDocumentValidationResult? Validation { get; set; }
    public string RollbackMethod { get; set; } = string.Empty;
    public string ApplyState { get; set; } = "Pending";
    public bool RestorationVerified { get; set; }
    public string? ActualRestoredHash { get; set; }
}

public sealed class PlcCodeTransactionTargetSnapshot
{
    public PlcBlockIdentity Target { get; set; } = new PlcBlockIdentity();
    public string Action { get; set; } = string.Empty;
    public string BaselineHash { get; set; } = "missing";
    public string RollbackMethod { get; set; } = string.Empty;
    public string? CanonicalResultHash { get; set; }
    public bool RestorationVerified { get; set; }
    public string? ActualRestoredHash { get; set; }
}

public sealed class PlcCodeTransactionJournalEntry
{
    public DateTimeOffset TimestampUtc { get; set; }
    public int? OperationSequence { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class PlcCodeTransactionCompileEvidence
{
    public bool Success { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PlcCodeTransactionApplyResult
{
    public string TransactionId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public bool Verified { get; set; }
    public bool RollbackAttempted { get; set; }
    public bool RolledBackAndVerified { get; set; }
    public bool RecoveryRequired { get; set; }
    public string Message { get; set; } = string.Empty;
    public PlcCodeTransaction Transaction { get; set; } = new PlcCodeTransaction();
}
