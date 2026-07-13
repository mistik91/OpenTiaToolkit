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
    public string SchemaVersion { get; set; } = "1.0";
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
    public string CompilePolicy { get; set; } = "NoNewErrors";
    public int? MaximumNewWarnings { get; set; }
    public string? ApplyToken { get; set; }
    public string? PlanHash { get; set; }
    public PlcCodeTransactionCompileEvidence? BaselineCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? FinalCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? RollbackCompilation { get; set; }
    public IReadOnlyList<PlcCodeTransactionOperation> Operations { get; set; } = Array.Empty<PlcCodeTransactionOperation>();
    public IReadOnlyList<PlcCodeTransactionJournalEntry> Journal { get; set; } = Array.Empty<PlcCodeTransactionJournalEntry>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string? Failure { get; set; }
    public bool RecoveryPackageRetained { get; set; }
}

public sealed class PlcCodeTransactionOperation
{
    public int Sequence { get; set; }
    public string Kind { get; set; } = "UpsertBlockDocuments";
    public string? SourcePath { get; set; }
    public string? SourceName { get; set; }
    public string? BaselinePath { get; set; }
    public string? ValidationBaselineHash { get; set; }
    public bool AllowTopologyChange { get; set; }
    public string ImportDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string ImportOption { get; set; } = "Override";
    public string Action { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string BaselineHash { get; set; } = string.Empty;
    public string? CanonicalResultHash { get; set; }
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
