using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.AutomationMl;
using OpenTiaToolkit.Core.Services.Hardware;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using Siemens.Engineering;
using Siemens.Engineering.Cax;

namespace OpenTiaToolkit.Core.Services.V21;

internal sealed class V21AutomationMlAccess(EngineeringSessionContext session, V21AutomationMlAdditions additions)
{
    private const string AdditiveTransactionDescription = "OpenTiaToolkit additive AutomationML reconciliation";
    private readonly HardwareEngineeringContext _engineering = new(session);

    public CommandOutcome Reconcile(string sourcePath, bool dryRun)
    {
        using var sourceStream = File.OpenRead(sourcePath);
        var source = AmlReader.Read(sourceStream);
        if (!source.Objects.Any(item => item.Kind == "Device"))
            throw new InvalidDataException("The AR APC document must contain at least one device.");

        return session.RunExclusive(() => Reconcile(source, sourceStream, dryRun));
    }

    private CommandOutcome Reconcile(AmlModel source, Stream sourceStream, bool dryRun)
    {
        var project = _engineering.RequireProject();
        var provider = project.GetService<CaxProvider>()
            ?? throw new InvalidOperationException("The selected project does not expose CAx transfer.");
        var directory = Path.Combine(Path.GetTempPath(), "ott-cax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input.aml");
        var snapshotPath = Path.Combine(directory, "before.aml");
        var verificationPath = Path.Combine(directory, "after.aml");
        var rollbackPath = Path.Combine(directory, "rollback.aml");

        try
        {
            var empty = IsHardwareEmpty(project);
            using var access = !empty && !dryRun && session.Session == null
                ? _engineering.RequirePortal().ExclusiveAccess(AdditiveTransactionDescription)
                : null;
            var current = empty ? AmlModel.Empty : Snapshot(project, provider, snapshotPath);
            var plan = AmlComparer.Compare(source, current);
            var additive = empty ? null : AmlAdditivePlanner.Create(source, current, plan);
            IReadOnlyDictionary<string, string>? catalog = null;
            if (additive?.CanApply == true)
            {
                try { catalog = additions.ResolveCatalog(_engineering.RequirePortal(), additive); }
                catch (Exception ex)
                {
                    additive.Conflicts.Add(new AmlDifference("CatalogConflict", project.Name, actual: EngineeringFailure.Describe(ex)));
                }
            }
            var planData = new
            {
                project = project.Name,
                source.ApcVersion,
                plan,
                strategy = empty ? "GreenfieldCax" : "Additive",
                additive,
                catalog,
                canApply = session.Session == null && plan.SourceDiagnostics.Count == 0 &&
                    plan.ProjectDiagnostics.Count == 0 && (empty || additive?.CanApply == true)
            };
            if (plan.Matches)
                return new CommandOutcome(true, plan.FullyVerified
                    ? "The selected project already matches the AutomationML configuration."
                    : "No configuration changes are needed. Review the catalog identifiers with limited readback.", planData);
            if (dryRun)
                return new CommandOutcome(true, "Compared AutomationML with the selected project.", planData);
            if (!planData.canApply)
                return new CommandOutcome(false, "Review the source diagnostics, project scope, and additive conflicts before applying.", planData);
            if (!empty)
                return ApplyAdditions(access!, project, provider, source, current, planData, additive!, catalog!, verificationPath, rollbackPath);

            // Keep the parsed input locked and stage the same bytes under the extension expected by CAx.
            sourceStream.Position = 0;
            using (var output = File.Create(inputPath))
                sourceStream.CopyTo(output);

            // CAx cannot run in an Openness exclusive-access scope. Recheck immediately before transfer.
            if (!IsHardwareEmpty(project))
                return new CommandOutcome(false, "Project hardware changed during planning. Generate a fresh plan.", planData);

            TransferResult result;
            try
            {
                result = provider.Import(new FileInfo(inputPath), CaxImportOptions.RetainTiaDevice);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "CAx transfer failed. Inspect the selected project's current state before retrying.", ex);
            }

            try
            {
                var verification = AmlComparer.Compare(source, Snapshot(project, provider, verificationPath));
                var verified = result.ErrorCount == 0 && result.State != TransferResultState.Error && verification.Matches;
                return new CommandOutcome(verified,
                    verified ? VerificationMessage(verification) :
                        "CAx transfer did not verify. Review the live project and the remaining differences before saving.",
                    new { before = planData, transfer = TransferSummary(result), verification });
            }
            catch (Exception ex)
            {
                return new CommandOutcome(false,
                    "CAx transfer ran, but project readback failed. Inspect the project before saving.",
                    new { before = planData, transfer = TransferSummary(result), verificationError = EngineeringFailure.Describe(ex) });
            }
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(snapshotPath);
            File.Delete(verificationPath);
            File.Delete(rollbackPath);
            Directory.Delete(directory);
        }
    }

    private CommandOutcome ApplyAdditions(ExclusiveAccess access, ProjectBase project, CaxProvider provider,
        AmlModel source, AmlModel baseline, object before, AmlAdditivePlan plan,
        IReadOnlyDictionary<string, string> catalog, string verificationPath, string rollbackPath)
    {
        var transactionState = new HardwarePatchResult { TransactionDescription = AdditiveTransactionDescription };
        AmlPlan? verification = null;
        AmlPlan? preservation = null;

        try
        {
            var transaction = access.Transaction(project, AdditiveTransactionDescription);
            HardwarePatchTransactionPolicy.Execute(transaction, () =>
            {
                additions.Apply(project, plan, catalog);
                var after = Snapshot(project, provider, verificationPath);
                verification = AmlComparer.Compare(source, after);
                preservation = AmlComparer.Compare(baseline, after);
                if (!verification.Matches || !preservation.Matches)
                    throw new InvalidOperationException("The additions or preservation of the existing configuration could not be verified.");
            }, transaction.CommitOnDispose, transactionState);

            return new CommandOutcome(true, "Added and verified the AutomationML configuration.",
                new { before, verification, preservation });
        }
        catch (Exception ex)
        {
            var rollbackVerified = false;
            if (transactionState.RolledBack)
            {
                try { rollbackVerified = EquivalentState(baseline, Snapshot(project, provider, rollbackPath)); }
                catch { /* Report the rollback as unverified if CAx readback is unavailable. */ }
            }

            var failure = session.RecordReadFailure(ex);
            var message = rollbackVerified ? "The additive apply failed and rollback was verified."
                : "The additive apply failed. Review the project state before retrying.";
            return new CommandOutcome(false, message, new
            {
                before, verification, preservation, failure = EngineeringFailure.Describe(failure),
                transactionState.RollbackAttempted, transactionState.RolledBack, rollbackVerified
            });
        }
    }

    private static bool EquivalentState(AmlModel first, AmlModel second) =>
        AmlComparer.Compare(first, second).Matches && AmlComparer.Compare(second, first).Matches;

    private static string VerificationMessage(AmlPlan verification) => verification.FullyVerified
        ? "Imported and verified the AutomationML configuration."
        : "Imported the AutomationML configuration. Review the catalog identifiers with limited readback.";

    private static bool IsHardwareEmpty(ProjectBase project) =>
        !ProjectDeviceEnumerator.EnumerateDevices(project).Any()
        && !project.UngroupedDevicesGroup.Devices.Any()
        && !project.Subnets.Any();

    private static AmlModel Snapshot(ProjectBase project, CaxProvider provider, string path)
    {
        var result = provider.Export(project, new FileInfo(path));
        if (result.ErrorCount != 0 || result.State == TransferResultState.Error)
            throw new InvalidOperationException($"CAx export failed with {result.ErrorCount} errors ({result.State}).");
        using var input = File.OpenRead(path);
        return AmlReader.Read(input, projectSnapshot: true);
    }

    private static object TransferSummary(TransferResult result) => new
    {
        state = result.State.ToString(), result.ErrorCount, result.WarningCount,
        messages = TransferMessages(result.Messages).ToArray()
    };

    private static IEnumerable<string> TransferMessages(TransferResultMessageComposition messages)
    {
        foreach (var message in messages)
        {
            if (message.State is TransferResultState.Warning or TransferResultState.Error)
                yield return message.Message;
            foreach (var child in TransferMessages(message.Messages))
                yield return child;
        }
    }

    internal sealed class CommandOutcome(bool success, string message, object data)
    {
        public bool Success { get; } = success;
        public string Message { get; } = message;
        public object Data { get; } = data;
    }
}
