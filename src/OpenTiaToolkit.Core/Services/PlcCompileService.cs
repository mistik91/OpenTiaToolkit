using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC compile operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcCompileService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21CompilationAccess _compilation;
        private readonly ILogger? _logger;

        public PlcCompileService(EngineeringSessionContext sessionContext, ILogger? logger, V21CompilationAccess compilation)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _logger = logger;
        }

        public async Task<CompilationResult> CompileSoftwareAsync(string? password = null)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    return new CompilationResult(false, 0, 0, "No PLC software is currently open");
                }

                _logger?.LogInformation("Compiling PLC software: {PlcName}", plcName);

                try
                {
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        _logger?.LogWarning("Password parameter is currently ignored by the V21 compile service");
                    }

                    var compileResult = _compilation.Compile(plcSoftware);
                    if (!compileResult.HasValue)
                    {
                        return new CompilationResult(false, 0, 0, "PLC software does not expose a supported V21 compile surface");
                    }

                    var nonNullCompileResult = compileResult.Value;
                    var errorCount = nonNullCompileResult.ErrorCount;
                    var warningCount = nonNullCompileResult.WarningCount;
                    var state = nonNullCompileResult.State;
                    var success = !string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase) && errorCount == 0;
                    var message = BuildCompilationMessage(nonNullCompileResult.Message, state, errorCount, warningCount);

                    return new CompilationResult(success, errorCount, warningCount, message, nonNullCompileResult.Diagnostics);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to compile PLC software");
                    return new CompilationResult(false, 1, 0, $"Compilation failed: {ex.Message}");
                }
            });
        }

        private static string BuildCompilationMessage(string? message, string state, int errorCount, int warningCount)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message!;
            }

            return $"Compilation {state}. Errors: {errorCount}, Warnings: {warningCount}.";
        }
    }
}
