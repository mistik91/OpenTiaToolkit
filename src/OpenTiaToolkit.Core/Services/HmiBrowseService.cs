using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Read-only Unified HMI browse and inspection operations.
    /// </summary>
    internal sealed class HmiBrowseService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger? _logger;

        public HmiBrowseService(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        public async Task<IReadOnlyList<HmiScreenInfo>> GetScreensAsync(string? groupPath, bool recursive)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return (IReadOnlyList<HmiScreenInfo>)Array.Empty<HmiScreenInfo>();
                }

                return _adapter.ListScreens(software, groupPath, recursive);
            });
        }

        public async Task<HmiScreenInfo?> GetScreenInfoAsync(string screenPath)
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

                return _adapter.ToScreenInfo(screen, normalizedPath, includeItems: true);
            });
        }

        public async Task<IReadOnlyList<HmiItemTypeInfo>> GetSupportedItemTypesAsync(string? filter)
        {
            return await Task.Run(() => _adapter.ListSupportedItemTypes(filter));
        }

        public async Task<IReadOnlyList<HmiTagInfo>> GetTagsAsync(string? filter, string? rootPath = null)
        {
            return await GetTagsAsync(new HmiTagBrowseOptions
            {
                Filter = filter,
                RootPath = rootPath
            });
        }

        public async Task<IReadOnlyList<HmiTagInfo>> GetTagsAsync(HmiTagBrowseOptions options)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return (IReadOnlyList<HmiTagInfo>)Array.Empty<HmiTagInfo>();
                }

                return _adapter.ListTags(software, options);
            });
        }

        public async Task<IReadOnlyList<HmiItemPropertyInfo>> GetItemPropertiesAsync(
            string screenPath,
            string itemName,
            string[]? propertyNames)
        {
            return await Task.Run(() =>
            {
                var item = TryGetScreenItem(screenPath, itemName, out _);
                return item == null
                    ? (IReadOnlyList<HmiItemPropertyInfo>)Array.Empty<HmiItemPropertyInfo>()
                    : _adapter.GetItemProperties(item, propertyNames);
            });
        }

        private HmiScreenItemBase? TryGetScreenItem(string screenPath, string itemName, out HmiScreen? screen)
        {
            screen = null;

            var software = _sessionContext.HmiSoftware;
            if (software == null)
            {
                _logger?.LogWarning("No Unified HMI is currently open");
                return null;
            }

            if (!_adapter.TryGetScreen(software, screenPath, out screen, out _ ) || screen == null)
            {
                _logger?.LogWarning("Unified HMI screen not found: {ScreenPath}", screenPath);
                return null;
            }

            var item = screen.ScreenItems.Find(itemName);
            if (item == null)
            {
                _logger?.LogWarning("Unified HMI item not found: {ScreenPath}/{ItemName}", screenPath, itemName);
            }

            return item;
        }
    }
}
