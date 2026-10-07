using System;
using OpenTiaToolkit.Application.Execution;

namespace OpenTiaToolkit.Tests.Application;

[TestClass]
public class WorkerDiagnosticAssessmentTests
{
    [TestMethod]
    public void BinaryDriftWithCompatibleApi_IsHealthyWithVisibleWarning()
    {
        var result = WorkerDiagnosticAssessment.Evaluate(true, Array.Empty<string>(), true,
            new[] { "Siemens.Engineering.Startdrive.dll" }, true);
        Assert.IsTrue(result.Healthy);
        Assert.AreEqual(0, result.Failures.Count);
        StringAssert.Contains(result.Warnings[0], "Startdrive.dll");
        StringAssert.Contains(result.Warnings[0], "do not prove live behavior");
    }

    [TestMethod]
    public void BinaryDriftDoesNotWaiveBreakingApi()
    {
        var result = WorkerDiagnosticAssessment.Evaluate(true, Array.Empty<string>(), true,
            new[] { "Siemens.Engineering.Startdrive.dll" }, false);
        Assert.IsFalse(result.Healthy);
        StringAssert.Contains(result.Failures[0], "breaking differences");
    }

    [TestMethod]
    public void MissingAssembly_IsNamedInFailure()
    {
        var result = WorkerDiagnosticAssessment.Evaluate(true, new[] { "Siemens.Engineering.Base.dll" },
            true, Array.Empty<string>(), true);
        Assert.IsFalse(result.Healthy);
        StringAssert.Contains(result.Failures[0], "Base.dll");
    }

    [TestMethod]
    public void MissingProfile_RemainsFailure()
    {
        var result = WorkerDiagnosticAssessment.Evaluate(true, Array.Empty<string>(), false, Array.Empty<string>(), true);
        Assert.IsFalse(result.Healthy);
        StringAssert.Contains(result.Failures[0], "profile is missing");
    }

    [TestMethod]
    public void WrongArchitecture_IsNamedInFailure()
    {
        var result = WorkerDiagnosticAssessment.Evaluate(false, Array.Empty<string>(), true, Array.Empty<string>(), true);
        Assert.IsFalse(result.Healthy);
        StringAssert.Contains(result.Failures[0], "64-bit");
    }
}
