using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Download;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System.Linq.Expressions;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal sealed class V21EngineeringObjectAccess
    {


        public string? GetTypeIdentifier(object engineeringObject)
        {
            return TiaReflectionHelper.GetProperty(engineeringObject, "TypeIdentifier")?.ToString();
        }

        public string? GetName(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetName(engineeringObject);
        }

        public string? GetRuntimeTypeName(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetTypeName(engineeringObject);
        }

        public string? GetDataType(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return (TiaReflectionHelper.GetProperty(engineeringObject, "DataTypeName")
                    ?? TiaReflectionHelper.GetProperty(engineeringObject, "DataType"))
                ?.ToString();
        }

        public string? GetLogicalAddress(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetProperty(engineeringObject, "LogicalAddress")?.ToString();
        }

        public void SetName(object engineeringObject, string value)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (value == null) throw new ArgumentNullException(nameof(value));

            TiaReflectionHelper.SetProperty(engineeringObject, "Name", value);
        }

        public void SetLogicalAddress(object engineeringObject, string value)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (value == null) throw new ArgumentNullException(nameof(value));

            TiaReflectionHelper.SetProperty(engineeringObject, "LogicalAddress", value);
        }

        public void SetDataType(object engineeringObject, string value)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (value == null) throw new ArgumentNullException(nameof(value));

            var propertyName = engineeringObject.GetType().GetProperty("DataTypeName") != null
                ? "DataTypeName"
                : "DataType";
            TiaReflectionHelper.SetProperty(engineeringObject, propertyName, value);
        }

        public string? GetTagCommentText(object tag, string culture = "en-US")
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (string.IsNullOrWhiteSpace(culture)) throw new ArgumentException("Culture cannot be empty.", nameof(culture));

            try
            {
                var comment = TiaReflectionHelper.GetProperty(tag, "Comment");
                if (comment == null)
                {
                    return null;
                }

                foreach (var item in EnumerateCommentItems(comment))
                {
                    if (!string.Equals(GetCommentCultureName(item), culture, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return TiaReflectionHelper.GetProperty(item, "Text")?.ToString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool TrySetTagComment(object tag, string commentText, string culture = "en-US")
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (commentText == null) throw new ArgumentNullException(nameof(commentText));
            if (string.IsNullOrWhiteSpace(culture)) throw new ArgumentException("Culture cannot be empty.", nameof(culture));

            try
            {
                var comment = TiaReflectionHelper.GetProperty(tag, "Comment");
                if (comment == null)
                {
                    return false;
                }

                foreach (var item in EnumerateCommentItems(comment))
                {
                    if (!string.Equals(GetCommentCultureName(item), culture, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TiaReflectionHelper.SetProperty(item, "Text", commentText);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryDelete(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            var deleteMethod = engineeringObject.GetType().GetMethod("Delete", Type.EmptyTypes);
            if (deleteMethod == null)
            {
                return false;
            }

            deleteMethod.Invoke(engineeringObject, null);
            return true;
        }

        public bool IsConsistent(object engineeringObject)
        {
            var value = TiaReflectionHelper.GetProperty(engineeringObject, "IsConsistent");
            return value is not bool consistent || consistent;
        }

        private static IEnumerable<object> EnumerateCommentItems(object comment)
        {
            var items = TiaReflectionHelper.GetProperty(comment, "Items") as System.Collections.IEnumerable;
            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static string? GetCommentCultureName(object commentItem)
        {
            var directCulture = TiaReflectionHelper.GetProperty(commentItem, "Culture");
            var directCultureName = TiaReflectionHelper.GetProperty(directCulture, "Name")?.ToString();
            if (!string.IsNullOrWhiteSpace(directCultureName))
            {
                return directCultureName;
            }

            var language = TiaReflectionHelper.GetProperty(commentItem, "Language");
            var languageCulture = TiaReflectionHelper.GetProperty(language, "Culture");
            return TiaReflectionHelper.GetProperty(languageCulture, "Name")?.ToString();
        }
    }
}
