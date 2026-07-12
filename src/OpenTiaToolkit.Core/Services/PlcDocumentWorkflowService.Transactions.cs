using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed partial class PlcDocumentWorkflowService
{
    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions TransactionJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PlcCodeTransaction> BeginTransactionAsync(string plcName, string? description, string? compilePolicy = null, int? maximumNewWarnings = null)
    {
        string normalizedPolicy = string.IsNullOrWhiteSpace(compilePolicy) ? "NoNewErrors" : compilePolicy!;
        if (!new[] { "NoNewErrors", "CleanCompile" }.Contains(normalizedPolicy, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("compile-policy must be NoNewErrors or CleanCompile.");
        normalizedPolicy = normalizedPolicy.Equals("CleanCompile", StringComparison.OrdinalIgnoreCase) ? "CleanCompile" : "NoNewErrors";
        if (maximumNewWarnings < 0) throw new InvalidOperationException("maximum-new-warnings cannot be negative.");
        await EnsurePlcOpen(plcName).ConfigureAwait(false);
        string id = Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var transaction = new PlcCodeTransaction
        {
            TransactionId = id,
            Description = description ?? string.Empty,
            State = "Draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            TiaProcessId = _sessionContext.AttachedPortalProcessId,
            ProjectName = SessionProjectName(),
            ProjectPath = SessionProjectPath(),
            PlcName = plcName,
            CompilePolicy = normalizedPolicy,
            MaximumNewWarnings = maximumNewWarnings
        };
        AddJournal(transaction, null, "Lifecycle", "Draft", "Transaction created and bound to the current TIA project and PLC.");
        SaveTransaction(transaction);
        return transaction;
    }

    public PlcCodeTransaction AddTransactionOperation(string transactionId, string importDirectory, string fileName, string? groupPath, string? importOption)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        PlcDocumentValidationResult validation = Validate(importDirectory, fileName);
        if (!validation.Valid) throw new InvalidDataException("PLC documents are invalid: " + string.Join("; ", validation.Errors));
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = NormalizeGroupPath(groupPath),
            ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption!,
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            Validation = validation
        });
        transaction.Operations = operations;
        AddJournal(transaction, operations.Count, "Authoring", "Added", $"Added SIMATIC SD operation for '{validation.BlockType}:{validation.BlockName}'.");
        SaveTransaction(transaction);
        return transaction;
    }

    public async Task<PlcCodeTransaction> PlanTransactionAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        if (transaction.Operations.Count == 0) throw new InvalidOperationException("The transaction has no operations.");
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);

        string baselineRoot = TransactionBaselineRoot(transaction.TransactionId);
        Cleanup(baselineRoot);
        Directory.CreateDirectory(baselineRoot);
        var prepared = new List<PlcCodeTransactionOperation>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (PlcCodeTransactionOperation source in transaction.Operations.OrderBy(item => item.Sequence))
            {
                PlcDocumentValidationResult validation = Validate(source.ImportDirectory, source.FileName);
                if (!validation.Valid) throw new InvalidDataException($"Operation {source.Sequence} documents are invalid: {string.Join("; ", validation.Errors)}");
                if (!FixedEquals(source.InputHash, HashDocumentPair(source.ImportDirectory, source.FileName)))
                    throw new InvalidOperationException($"Operation {source.Sequence} input changed after it was added. Recreate the transaction.");
                PlcCodeTransactionOperation operation = PrepareTransactionOperation(transaction, source, validation, baselineRoot);
                string identityKey = operation.Target!.ToString();
                if (!identities.Add(identityKey)) throw new InvalidOperationException($"The transaction targets '{identityKey}' more than once. Combine the intended change into one operation.");
                prepared.Add(operation);
            }
        }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }

        CompilationResult baseline;
        try { baseline = await _plcService.CompileSoftwareAsync().ConfigureAwait(false); }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }
        transaction.Operations = prepared;
        transaction.BaselineCompilation = ToEvidence(baseline);
        transaction.PlanHash = ComputeTransactionPlanHash(transaction);
        transaction.ApplyToken = RandomToken();
        transaction.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(TransactionLifetime);
        transaction.State = "Planned";
        transaction.RecoveryPackageRetained = true;
        transaction.Warnings = prepared.SelectMany(item => item.Validation?.Warnings ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        AddJournal(transaction, null, "Planning", "Planned", $"Snapshotted {prepared.Count(item => item.Action == "Override")} existing target(s); {prepared.Count(item => item.Action == "Create")} target(s) will be created.");
        SaveTransaction(transaction);
        return transaction;
    }

    public async Task<PlcCodeTransactionApplyResult> ApplyTransactionAsync(string transactionId, string applyToken)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Planned");
        if (string.IsNullOrWhiteSpace(transaction.PlanHash) || !FixedEquals(transaction.PlanHash!, ComputeTransactionPlanHash(transaction)))
        {
            transaction.State = "Stale";
            transaction.ApplyToken = null;
            transaction.Failure = "The persistent transaction manifest changed after planning.";
            AddJournal(transaction, null, "Preflight", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }
        if (string.IsNullOrWhiteSpace(transaction.ApplyToken) || !FixedEquals(transaction.ApplyToken!, applyToken ?? string.Empty))
            throw new InvalidOperationException("The transaction apply token is invalid or already consumed.");
        if (!transaction.ExpiresAtUtc.HasValue || transaction.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            transaction.State = "Expired";
            transaction.ApplyToken = null;
            AddJournal(transaction, null, "Preflight", "Expired", "The transaction expired before apply.");
            SaveTransaction(transaction);
            throw new InvalidOperationException("The transaction expired. Create and review a new transaction.");
        }

        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        try
        {
            VerifyTransactionPreconditions(transaction);
        }
        catch (Exception ex)
        {
            transaction.State = "Stale";
            transaction.ApplyToken = null;
            transaction.Failure = Message(ex);
            AddJournal(transaction, null, "Preflight", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw;
        }

        transaction.ApplyToken = null;
        transaction.State = "Applying";
        AddJournal(transaction, null, "Apply", "Starting", "All transaction preconditions matched atomically; mutation is starting.");
        SaveTransaction(transaction);
        try
        {
            foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
            {
                AddJournal(transaction, operation.Sequence, "Apply", "Starting", $"Importing exact target '{operation.Target}'.");
                operation.ApplyState = "Starting";
                SaveTransaction(transaction);
                bool imported = await _plcService.ImportBlockFromDocumentsAsync(operation.ImportDirectory, operation.FileName, operation.Target!.GroupPath, operation.ImportOption).ConfigureAwait(false);
                ResolvedBlock? resolved = imported ? ResolveExact(RequireBlockRoot(), operation.Target, allowMissing: true) : null;
                if (!imported || resolved == null || !IdentityMatches(operation.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(operation.Target.Number)))
                    throw new InvalidOperationException($"Operation {operation.Sequence} did not produce the exact planned target '{operation.Target}'.");
                operation.ApplyState = "Applied";
                AddJournal(transaction, operation.Sequence, "Apply", "Applied", "Exact target imported and read back.");
                SaveTransaction(transaction);
            }

            transaction.State = "Verifying";
            AddJournal(transaction, null, "Verify", "Starting", "All operations applied; compiling and exporting canonical results.");
            SaveTransaction(transaction);
            CompilationResult compilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            transaction.FinalCompilation = ToEvidence(compilation);
            int baselineErrors = transaction.BaselineCompilation?.ErrorCount ?? 0;
            int baselineWarnings = transaction.BaselineCompilation?.WarningCount ?? 0;
            bool compileAccepted = transaction.CompilePolicy == "CleanCompile"
                ? compilation.Success && compilation.ErrorCount == 0
                : compilation.ErrorCount <= baselineErrors && (baselineErrors > 0 || compilation.Success);
            int newWarnings = Math.Max(0, compilation.WarningCount - baselineWarnings);
            if (!compileAccepted || (transaction.MaximumNewWarnings.HasValue && newWarnings > transaction.MaximumNewWarnings.Value))
                throw new InvalidOperationException($"Final compile violated policy {transaction.CompilePolicy} (baseline errors/warnings {baselineErrors}/{baselineWarnings}, final {compilation.ErrorCount}/{compilation.WarningCount}, new warnings {newWarnings}). {compilation.Message}");
            VerifyCanonicalResults(transaction);
            transaction.State = "Succeeded";
            transaction.RecoveryPackageRetained = false;
            transaction.Failure = null;
            AddJournal(transaction, null, "Lifecycle", "Succeeded", "Every exact target was exported canonically and the transaction introduced no compile errors.");
            SaveTransaction(transaction);
            Cleanup(TransactionBaselineRoot(transaction.TransactionId));
            return Result(transaction, applied: true, verified: true, rollbackAttempted: false, rolledBack: false, recoveryRequired: false, "Transaction applied, compiled, and verified successfully.");
        }
        catch (Exception ex)
        {
            transaction.Failure = Message(ex);
            AddJournal(transaction, null, "Apply", "Failed", transaction.Failure);
            SaveTransaction(transaction);
            return await RollbackTransactionAsync(transaction, "Automatic rollback after apply or verification failure.").ConfigureAwait(false);
        }
    }

    public async Task<PlcCodeTransactionApplyResult> RecoverTransactionAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId, updateExpiry: false);
        if (!new[] { "Applying", "Verifying", "RollingBack", "RecoveryRequired" }.Contains(transaction.State, StringComparer.Ordinal))
            throw new InvalidOperationException($"Transaction '{transaction.TransactionId}' is in state '{transaction.State}' and does not require recovery.");
        if (string.IsNullOrWhiteSpace(transaction.PlanHash) || !FixedEquals(transaction.PlanHash!, ComputeTransactionPlanHash(transaction)))
        {
            transaction.State = "RecoveryRequired";
            transaction.Failure = "The transaction manifest integrity check failed. Automatic recovery is unsafe; inspect the retained baseline package manually.";
            AddJournal(transaction, null, "Recovery", "IntegrityFailed", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        AddJournal(transaction, null, "Recovery", "Starting", "Recovery was explicitly requested; all operations will be restored conservatively.");
        SaveTransaction(transaction);
        return await RollbackTransactionAsync(transaction, "Explicit recovery completed.").ConfigureAwait(false);
    }

    public PlcCodeTransaction GetTransaction(string transactionId) => LoadTransaction(transactionId);

    public IReadOnlyList<PlcCodeTransaction> ListTransactions()
    {
        if (!Directory.Exists(TransactionRoot)) return Array.Empty<PlcCodeTransaction>();
        var transactions = new List<PlcCodeTransaction>();
        foreach (string directory in Directory.GetDirectories(TransactionRoot))
        {
            string id = Path.GetFileName(directory);
            try { transactions.Add(LoadTransaction(id)); }
            catch { }
        }
        return transactions.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
    }

    public bool DiscardTransaction(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId, updateExpiry: false);
        if (new[] { "Applying", "Verifying", "RollingBack", "RecoveryRequired" }.Contains(transaction.State, StringComparer.Ordinal))
            throw new InvalidOperationException("A transaction with unverified recovery state cannot be discarded. Recover it first.");
        string directory = TransactionDirectory(transaction.TransactionId);
        Cleanup(directory);
        return !Directory.Exists(directory);
    }

    private PlcCodeTransactionOperation PrepareTransactionOperation(PlcCodeTransaction transaction, PlcCodeTransactionOperation source, PlcDocumentValidationResult validation, string baselineRoot)
    {
        string normalizedGroup = NormalizeGroupPath(source.GroupPath);
        object root = RequireBlockRoot();
        if (ResolveGroup(root, normalizedGroup) == null) throw new InvalidOperationException($"PLC block group was not found: '{DisplayGroup(normalizedGroup)}'.");
        IReadOnlyList<ResolvedBlock> named = FindBlocks(root, validation.BlockName!);
        IReadOnlyList<ResolvedBlock> inGroup = named.Where(item => GroupEquals(item.Identity.GroupPath, normalizedGroup)).ToArray();
        if (inGroup.Count > 1) throw Ambiguous(validation.BlockName!, inGroup);
        if (inGroup.Count == 1 && !BlockTypeEquals(inGroup[0].Identity.BlockType, validation.BlockType!))
            throw new InvalidOperationException($"Target '{inGroup[0].Identity}' exists with type '{inGroup[0].Identity.BlockType}', but the document declares '{validation.BlockType}'.");
        if (string.IsNullOrEmpty(normalizedGroup) && inGroup.Count == 0 && named.Count > 0)
            throw new InvalidOperationException($"Block '{validation.BlockName}' exists outside the root group. Supply group-path explicitly. Candidates: {string.Join(", ", named.Select(item => item.Identity.ToString()))}");

        ResolvedBlock? existing = inGroup.SingleOrDefault();
        string baselineHash = "missing";
        if (existing != null)
        {
            string backup = TransactionOperationBaseline(transaction.TransactionId, source.Sequence);
            Directory.CreateDirectory(backup);
            if (!ExportExact(existing, backup)) throw new InvalidOperationException($"Existing block '{existing.Identity}' could not be snapshotted; the transaction cannot start safely.");
            baselineHash = HashDocumentPair(backup, existing.Identity.Name);
        }
        return new PlcCodeTransactionOperation
        {
            Sequence = source.Sequence,
            Kind = source.Kind,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = normalizedGroup,
            ImportOption = source.ImportOption,
            Action = existing == null ? "Create" : "Override",
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            BaselineHash = baselineHash,
            Target = existing?.Identity ?? new PlcBlockIdentity { PlcName = transaction.PlcName, GroupPath = normalizedGroup, Name = validation.BlockName!, BlockType = validation.BlockType!, Number = validation.BlockNumber },
            Validation = validation,
            RollbackMethod = existing == null ? "DeleteExactTarget" : "RestoreDocumentAndVerifyHash",
            ApplyState = "Pending"
        };
    }

    private void VerifyTransactionPreconditions(PlcCodeTransaction transaction)
    {
        object root = RequireBlockRoot();
        string currentRoot = Path.Combine(TransactionDirectory(transaction.TransactionId), "preflight");
        Cleanup(currentRoot);
        try
        {
            foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
            {
                if (!FixedEquals(operation.InputHash, HashDocumentPair(operation.ImportDirectory, operation.FileName)))
                    throw new InvalidOperationException($"Operation {operation.Sequence} input documents changed after planning.");
                ResolvedBlock? current = ResolveExact(root, operation.Target!, allowMissing: true);
                string actual = "missing";
                if (current != null)
                {
                    string output = Path.Combine(currentRoot, operation.Sequence.ToString());
                    Directory.CreateDirectory(output);
                    if (!ExportExact(current, output)) throw new InvalidOperationException($"Operation {operation.Sequence} exact target could not be exported during preflight.");
                    actual = HashDocumentPair(output, current.Identity.Name);
                }
                if (!FixedEquals(operation.BaselineHash, actual))
                    throw new InvalidOperationException($"Operation {operation.Sequence} exact target changed after planning (expected {operation.BaselineHash}, actual {actual}).");
            }
        }
        finally { Cleanup(currentRoot); }
    }

    private void VerifyCanonicalResults(PlcCodeTransaction transaction)
    {
        string resultRoot = Path.Combine(TransactionDirectory(transaction.TransactionId), "result");
        Cleanup(resultRoot);
        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
        {
            ResolvedBlock resolved = ResolveExact(RequireBlockRoot(), operation.Target!, allowMissing: false)!;
            if (!IdentityMatches(operation.Target!, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(operation.Target!.Number)))
                throw new InvalidOperationException($"Operation {operation.Sequence} canonical identity verification failed.");
            string output = Path.Combine(resultRoot, operation.Sequence.ToString());
            Directory.CreateDirectory(output);
            if (!ExportExact(resolved, output)) throw new InvalidOperationException($"Operation {operation.Sequence} canonical export failed.");
            operation.CanonicalResultHash = HashDocumentPair(output, resolved.Identity.Name);
            operation.ApplyState = "Verified";
            AddJournal(transaction, operation.Sequence, "Verify", "Verified", $"Canonical result hash {operation.CanonicalResultHash}.");
        }
    }

    private async Task<PlcCodeTransactionApplyResult> RollbackTransactionAsync(PlcCodeTransaction transaction, string reason)
    {
        transaction.State = "RollingBack";
        AddJournal(transaction, null, "Rollback", "Starting", reason);
        SaveTransaction(transaction);
        bool allRestored = true;
        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderByDescending(item => item.Sequence))
        {
            try
            {
                var plan = new PlcDocumentImportPlan
                {
                    PlcName = transaction.PlcName,
                    Target = operation.Target!,
                    FileName = operation.FileName,
                    BaselineHash = operation.BaselineHash
                };
                RollbackEvidence evidence = await RollbackAsync(plan, TransactionOperationBaseline(transaction.TransactionId, operation.Sequence)).ConfigureAwait(false);
                operation.RestorationVerified = evidence.OperationSucceeded && evidence.HashMatches;
                operation.ActualRestoredHash = evidence.ActualHash;
                operation.ApplyState = operation.RestorationVerified ? "RolledBack" : "RecoveryRequired";
                allRestored &= operation.RestorationVerified;
                AddJournal(transaction, operation.Sequence, "Rollback", operation.ApplyState, operation.RestorationVerified
                    ? $"Restoration verified ({evidence.ActualHash})."
                    : $"Restoration could not be proven (expected {evidence.ExpectedHash}, actual {evidence.ActualHash}).");
                SaveTransaction(transaction);
            }
            catch (Exception ex)
            {
                allRestored = false;
                operation.ApplyState = "RecoveryRequired";
                AddJournal(transaction, operation.Sequence, "Rollback", "Failed", Message(ex));
                SaveTransaction(transaction);
            }
        }

        bool compileRestored = false;
        try
        {
            CompilationResult rollbackCompilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            transaction.RollbackCompilation = ToEvidence(rollbackCompilation);
            int baselineErrors = transaction.BaselineCompilation?.ErrorCount ?? 0;
            compileRestored = rollbackCompilation.ErrorCount <= baselineErrors && (baselineErrors > 0 || rollbackCompilation.Success);
        }
        catch (Exception ex)
        {
            AddJournal(transaction, null, "RollbackCompile", "Failed", Message(ex));
        }
        bool proven = allRestored && compileRestored;
        transaction.State = proven ? "RolledBackAndVerified" : "RecoveryRequired";
        transaction.RecoveryPackageRetained = !proven;
        AddJournal(transaction, null, "Rollback", transaction.State, proven
            ? "All original hashes or missing targets were restored and the baseline compile condition was recovered."
            : "Rollback remains unverified. The baseline package is retained for explicit recovery or manual inspection.");
        SaveTransaction(transaction);
        if (proven) Cleanup(TransactionBaselineRoot(transaction.TransactionId));
        return Result(transaction, applied: false, verified: false, rollbackAttempted: true, rolledBack: proven, recoveryRequired: !proven,
            proven ? "Transaction failed and was rolled back with full restoration proof." : "Transaction failed and automatic rollback could not be fully proven; recovery is required.");
    }

    private async Task EnsureTransactionContextAsync(PlcCodeTransaction transaction)
    {
        if (transaction.TiaProcessId.HasValue && _sessionContext.AttachedPortalProcessId != transaction.TiaProcessId)
            throw new InvalidOperationException($"Transaction is bound to TIA process {transaction.TiaProcessId}, but the current process is {_sessionContext.AttachedPortalProcessId?.ToString() ?? "none"}.");
        string projectName = SessionProjectName();
        if (!string.Equals(transaction.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Transaction is bound to project '{transaction.ProjectName}', but the current project is '{projectName}'.");
        await EnsurePlcOpen(transaction.PlcName).ConfigureAwait(false);
    }

    private static PlcCodeTransactionCompileEvidence ToEvidence(CompilationResult result) => new PlcCodeTransactionCompileEvidence
    {
        Success = result.Success,
        ErrorCount = result.ErrorCount,
        WarningCount = result.WarningCount,
        Message = result.Message ?? string.Empty
    };

    private static PlcCodeTransactionApplyResult Result(PlcCodeTransaction transaction, bool applied, bool verified, bool rollbackAttempted, bool rolledBack, bool recoveryRequired, string message) => new PlcCodeTransactionApplyResult
    {
        TransactionId = transaction.TransactionId,
        State = transaction.State,
        Applied = applied,
        Verified = verified,
        RollbackAttempted = rollbackAttempted,
        RolledBackAndVerified = rolledBack,
        RecoveryRequired = recoveryRequired,
        Message = message,
        Transaction = transaction
    };

    private static void RequireState(PlcCodeTransaction transaction, string state)
    {
        if (!string.Equals(transaction.State, state, StringComparison.Ordinal)) throw new InvalidOperationException($"Transaction '{transaction.TransactionId}' must be in state '{state}', but is '{transaction.State}'.");
    }

    private string SessionProjectName() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetName(_sessionContext.Project) ?? string.Empty;
    private string SessionProjectPath() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetProperty(_sessionContext.Project, "Path")?.ToString() ?? string.Empty;
    private static string TransactionRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions");
    private static string TransactionDirectory(string id) => Path.Combine(TransactionRoot, ValidateTransactionId(id));
    private static string TransactionManifest(string id) => Path.Combine(TransactionDirectory(id), "manifest.json");
    private static string TransactionJournal(string id) => Path.Combine(TransactionDirectory(id), "journal.jsonl");
    private static string TransactionBaselineRoot(string id) => Path.Combine(TransactionDirectory(id), "baseline");
    private static string TransactionOperationBaseline(string id, int sequence) => Path.Combine(TransactionBaselineRoot(id), sequence.ToString());

    private static string ValidateTransactionId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Transaction id is invalid.");
        return id;
    }

    private static PlcCodeTransaction LoadTransaction(string id, bool updateExpiry = true)
    {
        string manifest = TransactionManifest(id);
        if (!File.Exists(manifest)) throw new InvalidOperationException($"PLC code transaction '{id}' was not found.");
        PlcCodeTransaction transaction = JsonSerializer.Deserialize<PlcCodeTransaction>(File.ReadAllText(manifest), TransactionJsonOptions)
            ?? throw new InvalidDataException($"PLC code transaction '{id}' manifest is invalid.");
        if (updateExpiry && transaction.State == "Planned" && transaction.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            transaction.State = "Expired";
            transaction.ApplyToken = null;
            AddJournal(transaction, null, "Lifecycle", "Expired", "Transaction expired without mutation.");
            SaveTransaction(transaction);
        }
        return transaction;
    }

    private static void SaveTransaction(PlcCodeTransaction transaction)
    {
        transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;
        string directory = TransactionDirectory(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        string manifest = Path.Combine(directory, "manifest.json");
        string temporary = Path.Combine(directory, "manifest.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction, TransactionJsonOptions));
        if (File.Exists(manifest)) File.Replace(temporary, manifest, null);
        else File.Move(temporary, manifest);
        string recovery = Path.Combine(directory, "recovery.json");
        if (transaction.State == "RecoveryRequired")
        {
            File.WriteAllText(recovery, JsonSerializer.Serialize(new
            {
                transaction.TransactionId,
                transaction.State,
                transaction.Failure,
                transaction.ProjectName,
                transaction.PlcName,
                transaction.Operations
            }, TransactionJsonOptions));
        }
        else if (File.Exists(recovery)) File.Delete(recovery);
    }

    private static void AddJournal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message)
    {
        var journal = transaction.Journal.ToList();
        var entry = new PlcCodeTransactionJournalEntry { TimestampUtc = DateTimeOffset.UtcNow, OperationSequence = operation, Phase = phase, State = state, Message = message };
        journal.Add(entry);
        transaction.Journal = journal;
        string directory = TransactionDirectory(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        File.AppendAllText(TransactionJournal(transaction.TransactionId), JsonSerializer.Serialize(entry, TransactionJsonOptions) + Environment.NewLine);
    }

    private static string ComputeTransactionPlanHash(PlcCodeTransaction transaction)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            transaction.SchemaVersion,
            transaction.TransactionId,
            transaction.TiaProcessId,
            transaction.ProjectName,
            transaction.ProjectPath,
            transaction.PlcName,
            transaction.CompilePolicy,
            transaction.MaximumNewWarnings,
            Operations = transaction.Operations.OrderBy(item => item.Sequence).Select(item => new
            {
                item.Sequence,
                item.Kind,
                item.ImportDirectory,
                item.FileName,
                item.GroupPath,
                item.ImportOption,
                item.Action,
                item.InputHash,
                item.BaselineHash,
                item.RollbackMethod,
                item.Target
            }).ToArray()
        });
        return Hash(System.Text.Encoding.UTF8.GetBytes(canonical));
    }

    private static string Message(Exception exception) => exception.InnerException?.Message ?? exception.Message;
}
