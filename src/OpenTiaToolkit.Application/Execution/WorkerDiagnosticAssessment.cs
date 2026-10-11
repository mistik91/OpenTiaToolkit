using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Application.Execution;

public sealed class WorkerDiagnosticAssessment
{
    public bool Healthy => Failures.Count == 0;
    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Warnings { get; }

    private WorkerDiagnosticAssessment(List<string> failures, List<string> warnings)
    {
        Failures = failures;
        Warnings = warnings;
    }

    public static WorkerDiagnosticAssessment Evaluate(
        bool process64Bit, IReadOnlyCollection<string> missingAssemblies,
        bool binaryProfileAvailable, IReadOnlyCollection<string> binaryMismatches, bool apiCompatible)
    {
        var failures = new List<string>();
        var warnings = new List<string>();
        if (!process64Bit) failures.Add("The isolated V21 worker is not running as a 64-bit process.");
        if (missingAssemblies.Count > 0)
            failures.Add("Required Siemens assemblies are missing: " + string.Join(", ", missingAssemblies) + ".");
        if (!binaryProfileAvailable) failures.Add("The Siemens binary compatibility profile is missing.");
        if (!apiCompatible) failures.Add("The installed Siemens public API has breaking differences from the V21 baseline.");
        if (binaryMismatches.Count > 0)
            warnings.Add("Siemens binary hashes differ from the validated baseline: " + string.Join(", ", binaryMismatches)
                + ". Review the installed Siemens updates and validate affected workflows; matching public API surfaces do not prove live behavior.");
        return new WorkerDiagnosticAssessment(failures, warnings);
    }
}
