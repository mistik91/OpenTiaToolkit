using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiJsonDocumentValidator
    {
        public const string SupportedSchemaVersion = "1.0";

        private static readonly HashSet<string> DocumentKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Export", "Patch" };

        private static readonly HashSet<string> ScreenActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Create", "Update", "Delete" };

        private static readonly HashSet<string> ItemActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Create", "Update", "Delete", "Recreate" };

        public static void Validate(HmiJsonDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (!string.Equals(document.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unsupported Unified HMI JSON schemaVersion '{document.SchemaVersion}'. Expected '{SupportedSchemaVersion}'.");
            }

            if (string.IsNullOrWhiteSpace(document.DocumentKind) || !DocumentKinds.Contains(document.DocumentKind))
            {
                throw new InvalidDataException(
                    $"Unsupported Unified HMI JSON documentKind '{document.DocumentKind}'. Expected Export or Patch.");
            }

            if (document.Screens == null)
            {
                throw new InvalidDataException("Unified HMI JSON screens collection cannot be null.");
            }

            var screenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var screen in document.Screens)
            {
                if (string.IsNullOrWhiteSpace(screen.Path))
                {
                    throw new InvalidDataException("Every Unified HMI JSON screen requires a non-empty path.");
                }

                if (!screenPaths.Add(screen.Path))
                {
                    throw new InvalidDataException($"Duplicate Unified HMI JSON screen path '{screen.Path}'.");
                }

                ValidateAction(screen.Action, ScreenActions, $"screen '{screen.Path}'");
                if (screen.Items == null)
                {
                    throw new InvalidDataException($"Unified HMI JSON screen '{screen.Path}' has a null items collection.");
                }

                var itemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in screen.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                    {
                        throw new InvalidDataException($"Every item on screen '{screen.Path}' requires a non-empty name.");
                    }

                    if (!itemNames.Add(item.Name))
                    {
                        throw new InvalidDataException(
                            $"Duplicate item name '{item.Name}' on Unified HMI JSON screen '{screen.Path}'.");
                    }

                    ValidateAction(item.Action, ItemActions, $"item '{screen.Path}/{item.Name}'");
                }
            }
        }

        private static void ValidateAction(string? action, ISet<string> allowedActions, string target)
        {
            if (!string.IsNullOrWhiteSpace(action) && !allowedActions.Contains(action!.Trim()))
            {
                throw new InvalidDataException(
                    $"Unsupported Unified HMI JSON action '{action}' for {target}. " +
                    $"Expected {string.Join(", ", allowedActions)}, or an omitted action for Update.");
            }
        }
    }
}
