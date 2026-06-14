using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Centralizes V21-specific Siemens type lookup and reflection-heavy operations.
    /// This keeps version-sensitive behavior in one place instead of spreading it across services.
    /// </summary>
    internal sealed class V21OpennessAdapter
    {
        private readonly ConcurrentDictionary<string, Type?> _typeByNameCache = new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Type?> _typeByFullNameCache = new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);

        static V21OpennessAdapter()
        {
            TiaPortalAssemblyResolver.Register();
        }

        public Type? FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (TryFindKnownSiemensType(typeName, out var knownType))
            {
                return knownType;
            }

            return _typeByNameCache.GetOrAdd(typeName, static (name, @this) =>
            {
                foreach (var assembly in @this.EnumerateCandidateAssemblies())
                {
                    try
                    {
                        var matches = assembly.GetTypes()
                            .Where(candidate => candidate.Name == name)
                            .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
                            .ToList();
                        var type = matches.Count == 1 ? matches[0] : null;
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                return null;
            }, this);
        }

        public Type? FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            return _typeByFullNameCache.GetOrAdd(fullName, static (name, @this) =>
            {
                foreach (var assembly in @this.EnumerateCandidateAssemblies())
                {
                    try
                    {
                        var type = assembly.GetType(name, throwOnError: false, ignoreCase: false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                return null;
            }, this);
        }

        public object? GetService(object engineeringObject, string typeName)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            var serviceType = FindTypeInAssembly(engineeringObject.GetType().Assembly, typeName)
                ?? FindTypeByName(typeName)
                ?? FindTypeByFullName(typeName);
            return serviceType == null ? null : GetService(engineeringObject, serviceType);
        }

        public object? GetService(object engineeringObject, Type serviceType)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));

            var getServiceMethod = engineeringObject.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetService" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);

            if (getServiceMethod == null)
            {
                return null;
            }

            try
            {
                var genericMethod = getServiceMethod.MakeGenericMethod(serviceType);
                return genericMethod.Invoke(engineeringObject, null);
            }
            catch
            {
                return null;
            }
        }

        public object? GetPlcSoftware(object deviceItem)
        {
            if (deviceItem == null) throw new ArgumentNullException(nameof(deviceItem));

            if (deviceItem is DeviceItem typedDeviceItem)
            {
                var typedSoftwareContainer = typedDeviceItem.GetService<SoftwareContainer>();
                if (typedSoftwareContainer?.Software is PlcSoftware containedPlcSoftware)
                {
                    return containedPlcSoftware;
                }
            }

            var directPlcSoftware = GetService(deviceItem, "PlcSoftware");
            if (directPlcSoftware != null)
            {
                return directPlcSoftware;
            }

            var softwareContainer = GetService(deviceItem, "SoftwareContainer");
            if (softwareContainer == null)
            {
                return null;
            }

            var software = TiaReflectionHelper.GetProperty(softwareContainer, "Software");
            if (software == null)
            {
                return null;
            }

            var plcSoftwareType = FindTypeInAssembly(software.GetType().Assembly, "PlcSoftware") ?? FindTypeByName("PlcSoftware");
            if (plcSoftwareType == null)
            {
                return software;
            }

            return plcSoftwareType.IsInstanceOfType(software) ? software : null;
        }

        public SafetyLoginResult LoginToSafetyOfflineProgram(object plcDeviceItem, string password)
        {
            if (plcDeviceItem == null) throw new ArgumentNullException(nameof(plcDeviceItem));
            if (password == null) throw new ArgumentNullException(nameof(password));

            var safetyAdministration = GetService(plcDeviceItem, "SafetyAdministration");
            if (safetyAdministration == null)
            {
                return new SafetyLoginResult(
                    success: false,
                    serviceAvailable: false,
                    passwordRequired: false,
                    alreadyLoggedIn: false,
                    message: "SafetyAdministration service is not available for the selected PLC. If the project contains fail-safe tags, log into the safety program manually in TIA Portal before applying changes.");
            }

            var isPasswordSet = GetBooleanProperty(safetyAdministration, "IsSafetyOfflineProgramPasswordSet");
            var isLoggedIn = GetBooleanProperty(safetyAdministration, "IsLoggedOnToSafetyOfflineProgram");

            if (isLoggedIn == true)
            {
                return new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: isPasswordSet == true,
                    alreadyLoggedIn: true,
                    message: "Already logged into the safety offline program.");
            }

            if (isPasswordSet == false)
            {
                return new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: false,
                    alreadyLoggedIn: false,
                    message: "No safety offline program password is set.");
            }

            var loginMethod = safetyAdministration.GetType()
                .GetMethod("LoginToSafetyOfflineProgram", new[] { typeof(SecureString) });
            if (loginMethod == null)
            {
                return new SafetyLoginResult(
                    success: false,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "SafetyAdministration does not expose LoginToSafetyOfflineProgram in this TIA Portal version. Log into the safety program manually in TIA Portal before applying changes.");
            }

            try
            {
                using var securePassword = ToSecureString(password);
                loginMethod.Invoke(safetyAdministration, new object[] { securePassword });

                return new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Logged into the safety offline program.");
            }
            catch (TargetInvocationException ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                return new SafetyLoginResult(
                    success: false,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Failed to log into the safety offline program: " + message);
            }
            catch (Exception ex)
            {
                return new SafetyLoginResult(
                    success: false,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Failed to log into the safety offline program: " + ex.Message);
            }
        }

        private static bool? GetBooleanProperty(object source, string propertyName)
        {
            var value = TiaReflectionHelper.GetProperty(source, propertyName);
            return value is bool boolValue ? boolValue : null;
        }

        private static SecureString ToSecureString(string password)
        {
            var secureString = new SecureString();
            foreach (var c in password)
            {
                secureString.AppendChar(c);
            }

            secureString.MakeReadOnly();
            return secureString;
        }

        public bool TryGetHmiDeviceClassification(object deviceItem, out string? typeInfo, out bool isConfirmedHmi)
        {
            if (deviceItem == null) throw new ArgumentNullException(nameof(deviceItem));

            if (GetService(deviceItem, "HmiTarget") != null)
            {
                typeInfo = "HMI_Regular";
                isConfirmedHmi = true;
                return true;
            }

            if (GetService(deviceItem, "HmiSoftware") != null)
            {
                typeInfo = "HMI_Unified";
                isConfirmedHmi = true;
                return true;
            }

            var typeIdentifier = GetTypeIdentifier(deviceItem);
            if (!string.IsNullOrWhiteSpace(typeIdentifier))
            {
                var nonNullTypeIdentifier = typeIdentifier!;
                if (nonNullTypeIdentifier.IndexOf("Comfort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf("Unified", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf("Mobile Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf(" TP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf(" MP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nonNullTypeIdentifier.IndexOf("WinCC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    typeInfo = "Device type: " + nonNullTypeIdentifier;
                    isConfirmedHmi = false;
                    return true;
                }
            }

            var itemName = TiaReflectionHelper.GetName(deviceItem);
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                var nonNullItemName = itemName!;
                if (nonNullItemName.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    typeInfo = "Detected by name pattern";
                    isConfirmedHmi = false;
                    return true;
                }
            }

            typeInfo = null;
            isConfirmedHmi = false;
            return false;
        }

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

        public IReadOnlyList<object>? GetTagTables(object plcSoftware)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var tagTableGroup = TiaReflectionHelper.GetTagTableGroup(plcSoftware);
            return TiaReflectionHelper.GetTagTables(tagTableGroup)?.ToList();
        }

        public IReadOnlyList<object>? GetTags(object tagTable)
        {
            if (tagTable == null) throw new ArgumentNullException(nameof(tagTable));

            return TiaReflectionHelper.GetTags(tagTable)?.ToList();
        }

        public object? FindTagTable(object plcSoftware, string tagTableName)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));
            if (string.IsNullOrWhiteSpace(tagTableName)) throw new ArgumentException("Tag table name cannot be empty.", nameof(tagTableName));

            var requestedName = tagTableName!;
            return GetTagTables(plcSoftware)?.FirstOrDefault(tagTable =>
                string.Equals(GetName(tagTable), requestedName, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryCreateTag(
            object tagTable,
            string tagName,
            string dataType,
            string logicalAddress,
            out object? createdTag,
            out string? failureReason)
        {
            if (tagTable == null) throw new ArgumentNullException(nameof(tagTable));
            if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("Tag name cannot be empty.", nameof(tagName));
            if (string.IsNullOrWhiteSpace(dataType)) throw new ArgumentException("Data type cannot be empty.", nameof(dataType));

            createdTag = null;
            failureReason = null;

            var tagsCollection = TiaReflectionHelper.GetProperty(tagTable, "Tags");
            if (tagsCollection == null)
            {
                failureReason = "Tags collection is null or not accessible";
                return false;
            }

            var createMethod = tagsCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string), typeof(string) });
            if (createMethod == null)
            {
                failureReason = "Tags.Create() method not found";
                return false;
            }

            try
            {
                createdTag = createMethod.Invoke(tagsCollection, new object[] { tagName, dataType, logicalAddress });
            }
            catch (TargetInvocationException ex)
            {
                failureReason = ex.InnerException?.Message ?? ex.Message;
                return false;
            }

            if (createdTag == null)
            {
                failureReason = "The Tags.Create() method returned null";
                return false;
            }

            return true;
        }

        public string? GetDataType(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetProperty(engineeringObject, "DataType")?.ToString();
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

        public IReadOnlyList<object>? FindGlobalDbs(object plcSoftware, IEnumerable<string>? targetBlockNames = null)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
            if (blockGroup == null)
            {
                return null;
            }

            var results = new List<object>();
            var nameFilter = CreateNameFilter(targetBlockNames);
            CollectRecursive(
                blockGroup,
                TiaReflectionHelper.GetBlocks,
                block =>
                {
                    if (nameFilter != null)
                    {
                        return nameFilter(GetName(block));
                    }

                    return string.Equals(GetRuntimeTypeName(block), "GlobalDB", StringComparison.Ordinal);
                },
                results);

            return results;
        }

        public IReadOnlyList<object>? FindUdts(object plcSoftware, IEnumerable<string>? targetTypeNames = null)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var typeGroup = TiaReflectionHelper.GetTypeGroup(plcSoftware);
            if (typeGroup == null)
            {
                return null;
            }

            var results = new List<object>();
            var nameFilter = CreateNameFilter(targetTypeNames);
            CollectRecursive(
                typeGroup,
                TiaReflectionHelper.GetTypes,
                type =>
                {
                    if (nameFilter != null)
                    {
                        return nameFilter(GetName(type));
                    }

                    return string.Equals(GetRuntimeTypeName(type), "PlcStruct", StringComparison.Ordinal);
                },
                results);

            return results;
        }

        public IReadOnlyList<object>? GetDbMembers(object block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            var structure = TiaReflectionHelper.GetBlockStructure(block);
            var composition = TiaReflectionHelper.GetStructureComposition(structure);
            return TiaReflectionHelper.GetCompositionMembers(composition)?.ToList();
        }

        public IReadOnlyList<object>? GetUdtMembers(object type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var structure = TiaReflectionHelper.GetBlockStructure(type);
            return TiaReflectionHelper.GetStructureMembers(structure)?.ToList();
        }

        public IReadOnlyList<object>? GetNestedMembers(object member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            var nestedComposition = TiaReflectionHelper.GetStructureComposition(member);
            return TiaReflectionHelper.GetCompositionMembers(nestedComposition)?.ToList();
        }

        public V21CompileResult? Compile(object plcSoftware)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            if (plcSoftware is not PlcSoftware typedPlcSoftware)
            {
                return null;
            }

            var compileService = typedPlcSoftware.GetService<ICompilable>();
            if (compileService == null)
            {
                return null;
            }

            var rawResult = compileService.Compile();

            return new V21CompileResult(
                rawResult.ErrorCount,
                rawResult.WarningCount,
                rawResult.State.ToString(),
                null,
                ExtractCompileDiagnostics(rawResult));
        }

        public bool TryCompareToOnline(object plcSoftware, out object? compareResult, out string? failureReason)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            compareResult = null;
            failureReason = null;

            var compareMethod = plcSoftware.GetType().GetMethod("CompareToOnline", Type.EmptyTypes);
            if (compareMethod == null)
            {
                failureReason = "PLC software does not expose CompareToOnline in this TIA Portal version.";
                return false;
            }

            try
            {
                compareResult = compareMethod.Invoke(plcSoftware, null);
                if (compareResult == null)
                {
                    failureReason = "CompareToOnline returned no result.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public IReadOnlyList<PlcSoftwareCompareEntry> ExtractCompareEntries(object compareResult)
        {
            if (compareResult == null) throw new ArgumentNullException(nameof(compareResult));

            var entries = new List<PlcSoftwareCompareEntry>();
            var rootElement = TiaReflectionHelper.GetProperty(compareResult, "RootElement");
            if (rootElement != null)
            {
                CollectCompareEntries(rootElement, string.Empty, entries);
            }

            return entries;
        }

        public bool TrySelectPlcSimulationAdvanced(object downloadProvider, out string? message)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            message = null;
            var configuration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            if (configuration == null)
            {
                message = "DownloadProvider.Configuration is not available.";
                return false;
            }

            var targetForSoftware = FindObjectByTypeName(configuration, "TargetForSoftware", maxDepth: 8);
            if (targetForSoftware == null)
            {
                message = "TargetForSoftware configuration was not found.";
                return false;
            }

            var currentSelectionProperty = targetForSoftware.GetType().GetProperty("CurrentSelection");
            if (currentSelectionProperty == null || !currentSelectionProperty.CanWrite)
            {
                message = "TargetForSoftware.CurrentSelection is not writable.";
                return false;
            }

            if (!TryCreateEnumValue(currentSelectionProperty.PropertyType, "PlcSimulationAdvanced", out var simulatorSelection))
            {
                message = "TargetForSoftwareSelections.PlcSimulationAdvanced is not available.";
                return false;
            }

            currentSelectionProperty.SetValue(targetForSoftware, simulatorSelection);
            return true;
        }

        public bool IsDownloadConfigurationReady(object downloadProvider)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            var configuration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            return TiaReflectionHelper.GetProperty(configuration, "IsConfigured") is bool configured && configured;
        }

        public bool TryDownloadSoftware(object downloadProvider, bool onlyChanges, out string? state, out string? failureReason)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            state = null;
            failureReason = null;

            var connectionConfiguration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            if (connectionConfiguration == null)
            {
                failureReason = "DownloadProvider.Configuration is not available.";
                return false;
            }

            var downloadMethod = downloadProvider.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "Download")
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 4 &&
                           parameters[0].ParameterType.Name == "IConfiguration" &&
                           parameters[3].ParameterType.IsEnum;
                });

            if (downloadMethod == null)
            {
                failureReason = "No compatible Download(IConfiguration, ..., DownloadOptions) overload was found.";
                return false;
            }

            var targetConfigurationType = downloadMethod.GetParameters()[0].ParameterType;
            var targetConfiguration = FindObjectAssignableToType(connectionConfiguration, targetConfigurationType, maxDepth: 8);
            if (targetConfiguration == null)
            {
                failureReason = "No configured PLCSIM target interface was found for the download.";
                return false;
            }

            var optionsType = downloadMethod.GetParameters()[3].ParameterType;
            var optionName = onlyChanges ? "SoftwareOnlyChanges" : "Software";
            if (!TryCreateEnumValue(optionsType, optionName, out var softwareOption))
            {
                failureReason = $"DownloadOptions.{optionName} is not available.";
                return false;
            }

            try
            {
                var downloadConfigurationDelegateType = downloadMethod.GetParameters()[1].ParameterType;
                var configureDelegate = CreateDownloadConfigurationDelegate(downloadConfigurationDelegateType);
                var postDownloadConfigurationDelegateType = downloadMethod.GetParameters()[2].ParameterType;
                var postConfigureDelegate = CreateDownloadConfigurationDelegate(postDownloadConfigurationDelegateType);
                var result = downloadMethod.Invoke(downloadProvider, new[] { targetConfiguration, configureDelegate, postConfigureDelegate, softwareOption });
                state = TiaReflectionHelper.GetProperty(result, "State")?.ToString()
                    ?? TiaReflectionHelper.GetProperty(result, "Result")?.ToString()
                    ?? result?.ToString();

                return !string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public string GetOnlineState(object onlineProvider)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            return TiaReflectionHelper.GetProperty(onlineProvider, "State")?.ToString() ?? "Unknown";
        }

        public bool TryGoOnline(object onlineProvider, out string? state, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            state = GetOnlineState(onlineProvider);
            failureReason = null;
            if (string.Equals(state, "Online", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var goOnline = onlineProvider.GetType().GetMethod("GoOnline", Type.EmptyTypes);
            if (goOnline == null)
            {
                failureReason = "OnlineProvider does not expose GoOnline.";
                return false;
            }

            try
            {
                var result = goOnline.Invoke(onlineProvider, null);
                state = result?.ToString() ?? GetOnlineState(onlineProvider);
                return string.Equals(state, "Online", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                state = GetOnlineState(onlineProvider);
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public bool TryGoOffline(object onlineProvider, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            failureReason = null;
            var state = GetOnlineState(onlineProvider);
            if (string.Equals(state, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var goOffline = onlineProvider.GetType().GetMethod("GoOffline", Type.EmptyTypes);
            if (goOffline == null)
            {
                failureReason = "OnlineProvider does not expose GoOffline.";
                return false;
            }

            try
            {
                goOffline.Invoke(onlineProvider, null);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public bool TryControlCpu(object onlineProvider, string action, out string? state, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            state = GetOnlineState(onlineProvider);
            failureReason = null;

            var candidateNames = string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase)
                ? new[] { "StopCpu", "StopPlc", "Stop", "SetCpuToStop" }
                : new[] { "StartCpu", "StartPlc", "Start", "Run", "SetCpuToRun" };

            var method = onlineProvider.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == 0 &&
                                             candidateNames.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase));

            if (method == null)
            {
                failureReason = $"TIA Openness OnlineProvider exposes no supported CPU {action} operation. Available invocations: " +
                                string.Join(", ", GetInvocationNames(onlineProvider));
                return false;
            }

            try
            {
                method.Invoke(onlineProvider, null);
                state = GetOnlineState(onlineProvider);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                state = GetOnlineState(onlineProvider);
                return false;
            }
        }

        private static IReadOnlyList<CompilationDiagnostic> ExtractCompileDiagnostics(CompilerResult rawResult)
        {
            var diagnostics = new List<CompilationDiagnostic>();
            AddCompileDiagnostics(rawResult.Messages, diagnostics);
            return diagnostics;
        }

        private static void AddCompileDiagnostics(
            CompilerResultMessageComposition? messages,
            List<CompilationDiagnostic> diagnostics)
        {
            if (messages == null)
            {
                return;
            }

            foreach (var message in messages)
            {
                diagnostics.Add(new CompilationDiagnostic(
                    message.State.ToString(),
                    message.Path,
                    null,
                    message.Description,
                    null));

                AddCompileDiagnostics(message.Messages, diagnostics);
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

        public object? GetProgrammingLanguageValue(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SCL", "SCL" },
                { "FBD", "FBD" },
                { "LAD", "LD" },
                { "STL", "STL" },
                { "GRAPH", "GRAPH" },
                { "PRODIAG", "ProDiag" },
                { "S7_PDIAG", "ProDiag" }
            };

            var requestedLanguage = language!;
            var targetName = languageMapping.TryGetValue(requestedLanguage, out var mappedName)
                ? mappedName
                : requestedLanguage;

            foreach (var value in Enum.GetValues(typeof(ProgrammingLanguage)))
            {
                if (value?.ToString()?.Equals(targetName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return value;
                }
            }

            return null;
        }

        public object? CreateFb(object blocksCollection, string blockName, object? programmingLanguageValue)
        {
            if (blocksCollection == null) throw new ArgumentNullException(nameof(blocksCollection));
            if (string.IsNullOrWhiteSpace(blockName)) throw new ArgumentException("Block name cannot be empty.", nameof(blockName));

            var blocksCollectionType = blocksCollection.GetType();

            if (programmingLanguageValue != null)
            {
                var createWithLanguage = blocksCollectionType.GetMethod(
                    "CreateFB",
                    new[] { typeof(string), typeof(bool), typeof(int), programmingLanguageValue.GetType() });

                if (createWithLanguage != null)
                {
                    return createWithLanguage.Invoke(blocksCollection, new object[] { blockName, true, 0, programmingLanguageValue });
                }
            }

            var createSimple = blocksCollectionType.GetMethod(
                "CreateFB",
                new[] { typeof(string), typeof(bool), typeof(int) });

            return createSimple?.Invoke(blocksCollection, new object[] { blockName, true, 0 });
        }

        public bool TryExportEngineeringObject(object engineeringObject, string exportPath, string preferredOptionName)
        {
            return TryExportEngineeringObject(engineeringObject, exportPath, preferredOptionName, out _);
        }

        public bool TryExportEngineeringObject(object engineeringObject, string exportPath, string preferredOptionName, out string? failureReason)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (string.IsNullOrWhiteSpace(exportPath)) throw new ArgumentException("Export path cannot be empty.", nameof(exportPath));

            failureReason = null;
            var exportOptionsType = FindTypeInAssembly(engineeringObject.GetType().Assembly, "ExportOptions")
                ?? typeof(ExportOptions);
            var preferredOptionValue = exportOptionsType?.GetField(preferredOptionName)?.GetValue(null);

            if (exportOptionsType != null && preferredOptionValue != null)
            {
                var exportWithOptions = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                if (TryInvoke(exportWithOptions, engineeringObject, new object[] { new FileInfo(exportPath), preferredOptionValue }, out var attemptedFailureReason))
                {
                    return true;
                }

                failureReason ??= attemptedFailureReason;
            }

            var exportWithFileInfo = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
            if (TryInvoke(exportWithFileInfo, engineeringObject, new object[] { new FileInfo(exportPath) }, out var fileInfoFailureReason))
            {
                return true;
            }

            failureReason ??= fileInfoFailureReason;

            var exportWithString = engineeringObject.GetType().GetMethod("Export", new[] { typeof(string) });
            if (TryInvoke(exportWithString, engineeringObject, new object[] { exportPath }, out var stringFailureReason))
            {
                return true;
            }

            failureReason ??= stringFailureReason;
            return false;
        }

        public bool TryExportAsDocuments(object engineeringObject, DirectoryInfo exportDirectory, string baseName, out string? state)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (exportDirectory == null) throw new ArgumentNullException(nameof(exportDirectory));
            if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

            state = null;
            var exportMethod = engineeringObject.GetType().GetMethod("ExportAsDocuments", new[] { typeof(DirectoryInfo), typeof(string) });
            if (exportMethod == null)
            {
                return false;
            }

            var result = exportMethod.Invoke(engineeringObject, new object[] { exportDirectory, baseName });
            state = TiaReflectionHelper.GetProperty(result, "State")?.ToString();
            return string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryImport(object collection, FileInfo fileInfo, int importMode, out object? result)
        {
            return TryImport(collection, fileInfo, importMode, out result, out _);
        }

        public bool TryImport(object collection, FileInfo fileInfo, int importMode, out object? result, out string? failureReason)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (fileInfo == null) throw new ArgumentNullException(nameof(fileInfo));

            result = null;
            failureReason = null;
            foreach (var importMethod in collection.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, "Import", StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length))
            {
                var parameters = importMethod.GetParameters();
                if (parameters.Length is < 2 or > 3 || parameters[0].ParameterType != typeof(FileInfo))
                {
                    continue;
                }

                if (!TryCreateImportArgument(parameters[1].ParameterType, importMode, out var importOptionValue))
                {
                    continue;
                }

                var arguments = new object?[parameters.Length];
                arguments[0] = fileInfo;
                arguments[1] = importOptionValue;

                if (parameters.Length == 3)
                {
                    if (!TryCreateDefaultArgument(parameters[2].ParameterType, out var extraArgument))
                    {
                        continue;
                    }

                    arguments[2] = extraArgument;
                }

                try
                {
                    result = importMethod.Invoke(collection, arguments);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                    // Try the next compatible overload if invocation fails.
                }
            }

            failureReason ??= "No compatible Import(FileInfo, import option) overload succeeded.";
            return false;
        }

        public bool TryImportFromDocuments(object collection, DirectoryInfo importDirectory, string baseName, int importMode, out object? result)
        {
            return TryImportFromDocuments(collection, importDirectory, baseName, importMode, out result, out _);
        }

        public bool TryImportFromDocuments(object collection, DirectoryInfo importDirectory, string baseName, int importMode, out object? result, out string? failureReason)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (importDirectory == null) throw new ArgumentNullException(nameof(importDirectory));
            if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

            result = null;
            failureReason = null;
            foreach (var importMethod in collection.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, "ImportFromDocuments", StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length))
            {
                var parameters = importMethod.GetParameters();
                if (parameters.Length != 3 ||
                    parameters[0].ParameterType != typeof(DirectoryInfo) ||
                    parameters[1].ParameterType != typeof(string))
                {
                    continue;
                }

                if (!TryCreateImportArgument(parameters[2].ParameterType, importMode, out var importOptionValue))
                {
                    continue;
                }

                try
                {
                    result = importMethod.Invoke(collection, new[] { (object)importDirectory, baseName, importOptionValue });
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                    // Try the next compatible overload if invocation fails.
                }
            }

            failureReason ??= "No compatible ImportFromDocuments(DirectoryInfo, string, import option) overload succeeded.";
            return false;
        }

        private static bool TryCreateImportArgument(Type parameterType, int importMode, out object? value)
        {
            if (parameterType == typeof(int))
            {
                value = importMode;
                return true;
            }

            if (parameterType.IsEnum)
            {
                value = Enum.ToObject(parameterType, importMode);
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryCreateDefaultArgument(Type parameterType, out object? value)
        {
            if (!parameterType.IsValueType)
            {
                value = null;
                return true;
            }

            value = Activator.CreateInstance(parameterType);
            return true;
        }

        private static Delegate CreateDownloadConfigurationDelegate(Type delegateType)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameter = invoke?.GetParameters().SingleOrDefault();
            if (invoke == null || parameter == null)
            {
                throw new InvalidOperationException("Unsupported download configuration delegate shape.");
            }

            var parameterExpression = Expression.Parameter(parameter.ParameterType, "downloadConfiguration");
            var body = Expression.Call(
                typeof(V21OpennessAdapter).GetMethod(nameof(ConfigurePlcSimulationAdvanced), BindingFlags.NonPublic | BindingFlags.Static)!,
                Expression.Convert(parameterExpression, typeof(object)));

            return Expression.Lambda(delegateType, body, parameterExpression).Compile();
        }

        private static void ConfigurePlcSimulationAdvanced(object downloadConfiguration)
        {
            var targetForSoftware = FindObjectByTypeName(downloadConfiguration, "TargetForSoftware", maxDepth: 8);
            if (targetForSoftware == null)
            {
                return;
            }

            var currentSelectionProperty = targetForSoftware.GetType().GetProperty("CurrentSelection");
            if (currentSelectionProperty == null || !currentSelectionProperty.CanWrite)
            {
                return;
            }

            if (TryCreateEnumValue(currentSelectionProperty.PropertyType, "PlcSimulationAdvanced", out var simulatorSelection))
            {
                currentSelectionProperty.SetValue(targetForSoftware, simulatorSelection);
            }
        }

        private static IReadOnlyList<string> GetInvocationNames(object engineeringObject)
        {
            if (engineeringObject is IEngineeringObject typedEngineeringObject)
            {
                try
                {
                    return typedEngineeringObject
                        .GetInvocationInfos()
                        .Select(info => info.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    // Fall back to reflective lookup below.
                }
            }

            var getInvocationInfos = engineeringObject.GetType().GetMethod("GetInvocationInfos", Type.EmptyTypes);
            if (getInvocationInfos == null)
            {
                return Array.Empty<string>();
            }

            try
            {
                var result = getInvocationInfos.Invoke(engineeringObject, null) as IEnumerable;
                if (result == null)
                {
                    return Array.Empty<string>();
                }

                return result
                    .Cast<object>()
                    .Select(info => TiaReflectionHelper.GetProperty(info, "Name")?.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static void CollectCompareEntries(object element, string parentPath, List<PlcSoftwareCompareEntry> entries)
        {
            var leftName = TiaReflectionHelper.GetProperty(element, "LeftName")?.ToString();
            var rightName = TiaReflectionHelper.GetProperty(element, "RightName")?.ToString();
            var state = TiaReflectionHelper.GetProperty(element, "ComparisonResult")?.ToString()
                ?? TiaReflectionHelper.GetProperty(element, "State")?.ToString()
                ?? "Unknown";
            var detail = TiaReflectionHelper.GetProperty(element, "DetailedInformation")?.ToString();
            var name = !string.IsNullOrWhiteSpace(leftName) ? leftName : rightName;
            var path = string.IsNullOrWhiteSpace(parentPath)
                ? name ?? "<root>"
                : string.IsNullOrWhiteSpace(name) ? parentPath : parentPath + "/" + name;

            entries.Add(new PlcSoftwareCompareEntry(
                path,
                state,
                leftName,
                rightName,
                detail,
                IsCompareDifferenceState(state)));

            var children = TiaReflectionHelper.GetProperty(element, "Elements") as IEnumerable;
            if (children == null)
            {
                return;
            }

            foreach (var child in children)
            {
                if (child != null)
                {
                    CollectCompareEntries(child, path, entries);
                }
            }
        }

        private static bool IsCompareDifferenceState(string state)
        {
            return state.IndexOf("Different", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   state.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryCreateEnumValue(Type enumType, string name, out object? value)
        {
            value = null;
            if (!enumType.IsEnum || !Enum.IsDefined(enumType, name))
            {
                return false;
            }

            value = Enum.Parse(enumType, name);
            return true;
        }

        private static object? FindObjectByTypeName(object root, string typeName, int maxDepth)
        {
            return FindObjectByTypeName(root, typeName, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static object? FindObjectAssignableToType(object root, Type targetType, int maxDepth)
        {
            return FindObjectAssignableToType(root, targetType, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static object? FindObjectAssignableToType(object? current, Type targetType, int remainingDepth, HashSet<object> visited)
        {
            if (current == null || remainingDepth < 0 || !visited.Add(current))
            {
                return null;
            }

            if (targetType.IsInstanceOfType(current) && !string.Equals(current.GetType().Name, "ConnectionConfiguration", StringComparison.Ordinal))
            {
                return current;
            }

            if (current is string)
            {
                return null;
            }

            if (current is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var match = FindObjectAssignableToType(item, targetType, remainingDepth - 1, visited);
                    if (match != null)
                    {
                        return match;
                    }
                }

                return null;
            }

            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                var match = FindObjectAssignableToType(value, targetType, remainingDepth - 1, visited);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static object? FindObjectByTypeName(object? current, string typeName, int remainingDepth, HashSet<object> visited)
        {
            if (current == null || remainingDepth < 0 || !visited.Add(current))
            {
                return null;
            }

            if (string.Equals(current.GetType().Name, typeName, StringComparison.Ordinal))
            {
                return current;
            }

            if (current is string)
            {
                return null;
            }

            if (current is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var match = FindObjectByTypeName(item, typeName, remainingDepth - 1, visited);
                    if (match != null)
                    {
                        return match;
                    }
                }

                return null;
            }

            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                var match = FindObjectByTypeName(value, typeName, remainingDepth - 1, visited);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public bool IsConsistent(object engineeringObject)
        {
            var value = TiaReflectionHelper.GetProperty(engineeringObject, "IsConsistent");
            return value is not bool consistent || consistent;
        }

        private static bool TryInvoke(MethodInfo? method, object target, object[] arguments, out string? failureReason)
        {
            failureReason = null;
            if (method == null)
            {
                failureReason = "Method overload was not found.";
                return false;
            }

            try
            {
                method.Invoke(target, arguments);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        private static string FormatInvocationException(Exception ex)
        {
            return ex is TargetInvocationException { InnerException: { } innerException }
                ? innerException.Message
                : ex.Message;
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

        private static Func<string?, bool>? CreateNameFilter(IEnumerable<string>? names)
        {
            if (names == null)
            {
                return null;
            }

            var normalizedNames = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            var nameSet = new HashSet<string>(normalizedNames, StringComparer.OrdinalIgnoreCase);

            if (nameSet.Count == 0)
            {
                return null;
            }

            return candidateName => candidateName != null && nameSet.Contains(candidateName);
        }

        private static void CollectRecursive(
            object group,
            Func<object?, IEnumerable<object>?> getItems,
            Func<object, bool> shouldInclude,
            List<object> results)
        {
            var items = getItems(group);
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (shouldInclude(item))
                    {
                        results.Add(item);
                    }
                }
            }

            var nestedGroups = TiaReflectionHelper.GetGroups(group);
            if (nestedGroups == null)
            {
                return;
            }

            foreach (var nestedGroup in nestedGroups)
            {
                CollectRecursive(nestedGroup, getItems, shouldInclude, results);
            }
        }

        private IEnumerable<Assembly> EnumerateCandidateAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies.Where(IsSiemensAssembly))
            {
                yield return assembly;
            }

            foreach (var assembly in assemblies.Where(assembly => !IsSiemensAssembly(assembly)))
            {
                yield return assembly;
            }
        }

        private static bool IsSiemensAssembly(Assembly assembly)
        {
            return assembly.FullName?.StartsWith("Siemens.Engineering", StringComparison.Ordinal) == true;
        }

        private static Type? FindTypeInAssembly(Assembly assembly, string typeNameOrFullName)
        {
            try
            {
                return assembly.GetType(typeNameOrFullName, throwOnError: false, ignoreCase: false)
                    ?? assembly.GetTypes().FirstOrDefault(type =>
                        type.Name == typeNameOrFullName ||
                        string.Equals(type.FullName, typeNameOrFullName, StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }

        private static bool TryFindKnownSiemensType(string typeName, out Type type)
        {
            switch (typeName)
            {
                case nameof(PlcSoftware):
                    type = typeof(PlcSoftware);
                    return true;
                case nameof(ProgrammingLanguage):
                    type = typeof(ProgrammingLanguage);
                    return true;
                case nameof(ExportOptions):
                    type = typeof(ExportOptions);
                    return true;
                case nameof(SoftwareContainer):
                    type = typeof(SoftwareContainer);
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }
    }

    internal readonly struct V21CompileResult
    {
        public V21CompileResult(int errorCount, int warningCount, string state, string? message, IReadOnlyList<CompilationDiagnostic>? diagnostics = null)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            State = state;
            Message = message;
            Diagnostics = diagnostics ?? Array.Empty<CompilationDiagnostic>();
        }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public string State { get; }

        public string? Message { get; }

        public IReadOnlyList<CompilationDiagnostic> Diagnostics { get; }
    }
}
