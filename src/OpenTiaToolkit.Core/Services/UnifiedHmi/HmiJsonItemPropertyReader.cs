using Siemens.Engineering.HmiUnified.UI.Base;
using System;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonItemPropertyReader
    {
        private readonly V21UnifiedHmiAdapter _adapter;

        internal HmiJsonItemPropertyReader(V21UnifiedHmiAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        internal bool TryGetValue(HmiScreenItemBase item, string propertyName, out string? value)
        {
            var property = _adapter.GetItemProperties(item, new[] { propertyName }).FirstOrDefault();
            value = property?.Value;
            return property != null;
        }
    }
}
