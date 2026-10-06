using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcCompileEvidenceTests
{
    [DataTestMethod]
    [DataRow("{\"success\":true,\"errorCount\":0,\"warningCount\":0,\"message\":\"legacy\"}")]
    [DataRow("{\"success\":true,\"message\":\"legacy\",\"diagnostics\":null}")]
    public void Deserialize_LegacyOrNullDiagnostics_DefaultsToEmpty(string json)
    {
        var evidence = JsonSerializer.Deserialize<PlcCodeTransactionCompileEvidence>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsTrue(evidence.Success);
        Assert.AreEqual("legacy", evidence.Message);
        Assert.AreEqual(0, evidence.Diagnostics.Count);
    }

    [TestMethod]
    public void CompileEvidence_SnapshotsAndRoundTripsEveryDiagnosticField()
    {
        var diagnostics = new List<CompilationDiagnostic>
        {
            new CompilationDiagnostic("Warning", "PLC/Program/Control", "Control", "Parameter '#Output' might not be initialized — überprüfen.", "W-42"),
            new CompilationDiagnostic("Information", null, null, null, null)
        };
        var evidence = PlcWorkflowPrimitives.CompileEvidence(new CompilationResult(true, 0, 1, null, diagnostics));
        diagnostics.Clear();
        Assert.AreEqual(2, evidence.Diagnostics.Count);
        var restored = JsonSerializer.Deserialize<PlcCodeTransactionCompileEvidence>(JsonSerializer.Serialize(evidence))!;
        Assert.IsTrue(restored.Success);
        Assert.AreEqual(0, restored.ErrorCount);
        Assert.AreEqual(1, restored.WarningCount);
        Assert.AreEqual(string.Empty, restored.Message);
        Assert.AreEqual(2, restored.Diagnostics.Count);
        Assert.AreEqual("Warning", restored.Diagnostics[0].Severity);
        Assert.AreEqual("PLC/Program/Control", restored.Diagnostics[0].Path);
        Assert.AreEqual("Control", restored.Diagnostics[0].Name);
        Assert.AreEqual("Parameter '#Output' might not be initialized — überprüfen.", restored.Diagnostics[0].Description);
        Assert.AreEqual("W-42", restored.Diagnostics[0].ErrorCode);
        Assert.AreEqual("Information", restored.Diagnostics[1].Severity);
        Assert.IsNull(restored.Diagnostics[1].Path);
        Assert.IsNull(restored.Diagnostics[1].Name);
        Assert.IsNull(restored.Diagnostics[1].Description);
        Assert.IsNull(restored.Diagnostics[1].ErrorCode);
    }

    [TestMethod]
    public void CompileEvidence_NoDiagnostics_RemainsAnEmptyCollection()
    {
        Assert.AreEqual(0, PlcWorkflowPrimitives.CompileEvidence(new CompilationResult(false, 1, 0, "failed")).Diagnostics.Count);
    }
}
