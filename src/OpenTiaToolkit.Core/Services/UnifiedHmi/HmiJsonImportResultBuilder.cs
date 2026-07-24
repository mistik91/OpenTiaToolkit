using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiJsonImportResultBuilder
    {
        internal static void AddObjectAction(HmiJsonImportResult result, string scope, string screenPath, string? itemName, string? itemType, string action, string reason)
        {
            result.ObjectActions.Add(new HmiJsonObjectAction
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                ItemType = itemType,
                Action = action,
                Reason = reason
            });
        }

        internal static void AddPropertyChange(HmiJsonImportResult result, string scope, string screenPath, string? itemName, string property, string action, string? currentValue, string? desiredValue)
        {
            result.PropertyChanges.Add(new HmiJsonPropertyChange
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Action = action,
                CurrentValue = currentValue,
                DesiredValue = desiredValue
            });
        }

        internal static void AddSkippedProperty(HmiJsonImportResult result, string scope, string screenPath, string? itemName, string property, string reason)
        {
            result.PropertySkippedCount++;
            result.SkippedProperties.Add(new HmiJsonPropertyIssue
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Reason = reason
            });
        }

        internal static void AddFailedProperty(HmiJsonImportResult result, string scope, string screenPath, string? itemName, string property, string reason)
        {
            result.PropertyFailedCount++;
            result.FailedProperties.Add(new HmiJsonPropertyIssue
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Reason = reason
            });
        }

        internal static void AddWarning(HmiJsonImportResult result, string warning) => result.Warnings.Add(warning);

        internal static string FormatObjectPath(string screenPath, string? itemName) =>
            string.IsNullOrWhiteSpace(itemName) ? screenPath : screenPath + "/" + itemName;

        internal static string FormatExceptionMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }

            return messages.Count == 0 ? ex.GetType().Name : string.Join(" | Inner: ", messages);
        }

        internal static string? SerializeCompositeValue<T>(T? value) where T : class =>
            value == null ? null : JsonSerializer.Serialize(value);
    }
}
