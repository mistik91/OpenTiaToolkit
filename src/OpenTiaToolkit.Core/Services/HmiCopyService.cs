using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Unified HMI screen copy operations.
    /// </summary>
    internal sealed class HmiCopyService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger? _logger;

        public HmiCopyService(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        public async Task<HmiScreenCopyResult?> CopyScreenFromTemplateAsync(
            string templateScreenPath,
            string targetScreenPath,
            string? findText,
            string? replaceText)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                if (!_adapter.TryCopyScreenFromTemplate(
                        software,
                        templateScreenPath,
                        targetScreenPath,
                        findText,
                        replaceText,
                        out var result,
                        out var error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        _logger?.LogWarning("Unified HMI screen copy failed: {Error}", error);
                    }

                    return null;
                }

                return result;
            });
        }
    }
}
