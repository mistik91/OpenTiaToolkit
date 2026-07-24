using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal sealed class V21HardwareCatalogAccess
    {
        public IReadOnlyList<HardwareCatalogItemInfo> FindCatalogItems(TiaPortal portal, string searchText, string? culture)
        {
            var entries = string.IsNullOrWhiteSpace(culture)
                ? portal.HardwareCatalog.Find(searchText ?? string.Empty)
                : portal.HardwareCatalog.Find(searchText ?? string.Empty, culture);
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

        public string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier)
        {
            var utilities = TiaReflectionHelper.GetProperty(project, "HwUtilities")
                ?? throw new InvalidOperationException("The current project does not expose hardware utilities.");
            var provider = Invoke(utilities, "Find", "ModuleInformationProvider");
            if (provider == null) return Array.Empty<string>();
            var method = provider.GetType().GetMethod("FindModuleTypes", new[] { typeof(string) });
            return method?.Invoke(provider, new object[] { partialTypeIdentifier ?? string.Empty }) as string[] ?? Array.Empty<string>();
        }

        private static object? Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new InvalidOperationException($"Method '{methodName}' is not available on {target.GetType().Name}.");
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }
    }
}
