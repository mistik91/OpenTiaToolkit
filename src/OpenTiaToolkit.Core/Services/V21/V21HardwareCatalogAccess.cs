using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal sealed class V21HardwareCatalogAccess
    {
        public IReadOnlyList<HardwareCatalogItemInfo> FindCatalogItems(TiaPortal portal, string searchText, string? typeIdentifier)
        {
            var entries = string.IsNullOrWhiteSpace(typeIdentifier)
                ? portal.HardwareCatalog.Find(searchText ?? string.Empty)
                : portal.HardwareCatalog.Find(searchText ?? string.Empty, typeIdentifier);
            return entries.Select(entry => new HardwareCatalogItemInfo
            {
                TypeIdentifier = entry.TypeIdentifier,
                TypeIdentifierNormalized = entry.TypeIdentifierNormalized,
                TypeName = entry.TypeName,
                ArticleNumber = entry.ArticleNumber,
                Description = entry.Description,
                Version = entry.Version,
                CatalogPath = entry.CatalogPath
            }).ToList();
        }

        public string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier) =>
            V21HardwareModuleTypeAccess.FindModuleTypes(project, partialTypeIdentifier);
    }
}
