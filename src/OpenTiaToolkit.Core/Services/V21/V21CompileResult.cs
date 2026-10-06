using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal readonly struct V21CompileResult
    {
        public V21CompileResult(int errorCount, int warningCount, string state, string? message, IReadOnlyList<CompilationDiagnostic>? diagnostics = null)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            State = state;
            Message = message;
            Diagnostics = diagnostics ?? Array.Empty<CompilationDiagnostic>();
        }

        public int ErrorCount { get; }
        public int WarningCount { get; }
        public string State { get; }
        public string? Message { get; }
        public IReadOnlyList<CompilationDiagnostic> Diagnostics { get; }
    }
}
