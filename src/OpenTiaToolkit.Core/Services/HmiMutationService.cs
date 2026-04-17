using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Unified HMI create and edit operations.
    /// </summary>
    internal sealed class HmiMutationService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger? _logger;

        public HmiMutationService(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        public async Task<HmiScreenInfo?> CreateScreenAsync(string screenPath, int? width, int? height)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                var screen = _adapter.EnsureScreen(software, screenPath);
                if (screen == null)
                {
                    return null;
                }

                if (width.HasValue && width.Value > 0)
                {
                    screen.Width = (uint)width.Value;
                }

                if (height.HasValue && height.Value > 0)
                {
                    screen.Height = (uint)height.Value;
                }

                return _adapter.ToScreenInfo(screen, _adapter.NormalizeScreenPath(screenPath), includeItems: false);
            });
        }

        public async Task<HmiScreenItemInfo?> CreateScreenItemAsync(
            string screenPath,
            string itemType,
            string itemName,
            int? left,
            int? top,
            int? width,
            int? height)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                if (!_adapter.TryGetScreen(software, screenPath, out var screen, out var normalizedPath) || screen == null)
                {
                    _logger?.LogWarning("Unified HMI screen not found: {ScreenPath}", screenPath);
                    return null;
                }

                var existingItem = screen.ScreenItems.Find(itemName);
                if (existingItem != null)
                {
                    return _adapter.ToItemInfo(existingItem, normalizedPath);
                }

                var item = _adapter.CreateScreenItem(screen, itemType, itemName);
                if (item == null)
                {
                    return null;
                }

                _adapter.TryApplyGeometry(item, left, top, width, height, out _);
                return _adapter.ToItemInfo(item, normalizedPath);
            });
        }

        public async Task<bool> SetItemPropertyAsync(string screenPath, string itemName, string propertyName, string value)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return false;
                }

                if (!_adapter.TryGetScreen(software, screenPath, out var screen, out _ ) || screen == null)
                {
                    _logger?.LogWarning("Unified HMI screen not found: {ScreenPath}", screenPath);
                    return false;
                }

                var item = screen.ScreenItems.Find(itemName);
                if (item == null)
                {
                    _logger?.LogWarning("Unified HMI item not found: {ScreenPath}/{ItemName}", screenPath, itemName);
                    return false;
                }

                return _adapter.TrySetItemProperty(item, propertyName, value, software, out _);
            });
        }
    }
}
