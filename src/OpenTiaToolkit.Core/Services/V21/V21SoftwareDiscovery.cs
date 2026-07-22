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
    internal sealed class V21SoftwareDiscovery
    {
        private readonly V21TypeServiceResolver _resolver;
        public V21SoftwareDiscovery(V21TypeServiceResolver resolver)
        {
            _resolver = resolver;
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

            var directPlcSoftware = _resolver.GetService(deviceItem, "PlcSoftware");
            if (directPlcSoftware != null)
            {
                return directPlcSoftware;
            }

            var softwareContainer = _resolver.GetService(deviceItem, "SoftwareContainer");
            if (softwareContainer == null)
            {
                return null;
            }

            var software = TiaReflectionHelper.GetProperty(softwareContainer, "Software");
            if (software == null)
            {
                return null;
            }

            var plcSoftwareType = _resolver.FindTypeByName("PlcSoftware");
            if (plcSoftwareType == null)
            {
                return software;
            }

            return plcSoftwareType.IsInstanceOfType(software) ? software : null;
        }

        public HmiSoftware? GetUnifiedHmiSoftware(object deviceItem)
        {
            if (deviceItem == null) throw new ArgumentNullException(nameof(deviceItem));

            if (deviceItem is DeviceItem typedDeviceItem)
            {
                var softwareContainer = typedDeviceItem.GetService<SoftwareContainer>();
                return softwareContainer?.Software as HmiSoftware;
            }

            var reflectedContainer = _resolver.GetService(deviceItem, typeof(SoftwareContainer));
            return TiaReflectionHelper.GetProperty(reflectedContainer, "Software") as HmiSoftware;
        }

        public SafetyLoginResult LoginToSafetyOfflineProgram(object plcDeviceItem, string password)
        {
            if (plcDeviceItem == null) throw new ArgumentNullException(nameof(plcDeviceItem));
            if (password == null) throw new ArgumentNullException(nameof(password));

            var safetyAdministration = _resolver.GetService(plcDeviceItem, "SafetyAdministration");
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

            var unifiedSoftware = GetUnifiedHmiSoftware(deviceItem);
            if (unifiedSoftware != null)
            {
                typeInfo = "Unified HMI software: " + unifiedSoftware.GetType().FullName;
                isConfirmedHmi = true;
                return true;
            }

            if (_resolver.GetService(deviceItem, "HmiTarget") != null)
            {
                typeInfo = "HMI_Regular";
                isConfirmedHmi = true;
                return true;
            }

            if (_resolver.GetService(deviceItem, "HmiSoftware") != null)
            {
                typeInfo = "HMI_Unified";
                isConfirmedHmi = true;
                return true;
            }

            var typeIdentifier = TiaReflectionHelper.GetProperty(deviceItem, "TypeIdentifier")?.ToString();
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
    }
}
