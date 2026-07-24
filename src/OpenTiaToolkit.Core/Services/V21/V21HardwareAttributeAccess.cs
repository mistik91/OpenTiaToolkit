using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services.V21
{
    /// <summary>
    /// Owns V21 hardware attributes, feature attributes, GSD parameter bytes, and ordinary IO addresses.
    /// </summary>
    internal sealed class V21HardwareAttributeAccess
    {
        private readonly V21TypeServiceResolver _services;

        public V21HardwareAttributeAccess(V21TypeServiceResolver services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public object? GetFeatureService(HardwareObject target, string serviceType) =>
            _services.GetService(target, serviceType);

        public IReadOnlyList<HardwareAttributeInfo> GetAttributeInfos(HardwareObject target, IEnumerable<string>? attributeNames)
        {
            var requested = Requested(attributeNames);
            if (requested.Length > 0)
                return requested.Select(name => new HardwareAttributeInfo { Name = name, Value = TryGetAttribute(target, name) }).ToList();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in target.GetAttributeInfos())
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(new HardwareAttributeInfo { Name = name!, IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool? });
            }
            return result;
        }

        public object? GetAttribute(HardwareObject target, string name) =>
            target.GetAttribute(name);

        public IReadOnlyList<HardwareIoAddressInfo> GetIoAddresses(
            DeviceItem target,
            HardwareObjectRef reference,
            IEnumerable<string>? attributeNames)
        {
            var result = new List<HardwareIoAddressInfo>();
            var index = 0;
            foreach (Address address in target.Addresses)
                result.Add(ToIoAddressInfo(address, reference, index++, attributeNames));
            return result;
        }

        public HardwareIoAddressChangeInfo SetIoAddressStart(
            DeviceItem target,
            int addressIndex,
            int startAddress,
            int? expectedStartAddress)
        {
            var addresses = target.Addresses.Cast<Address>().ToList();
            if (addressIndex < 0 || addressIndex >= addresses.Count)
                throw new InvalidOperationException($"Hardware item has no IO address at index {addressIndex}.");
            var address = addresses[addressIndex];
            var before = address.StartAddress;
            if (expectedStartAddress.HasValue && before != expectedStartAddress.Value)
                throw new InvalidOperationException($"IO address start differs from expectedStartAddress at index {addressIndex}.");
            address.StartAddress = startAddress;
            return new HardwareIoAddressChangeInfo
            {
                AddressIndex = addressIndex,
                IoType = Convert.ToString(address.IoType, CultureInfo.InvariantCulture),
                BeforeStartAddress = before,
                WrittenStartAddress = startAddress
            };
        }

        public IReadOnlyList<HardwareAttributeInfo> GetFeatureAttributeInfos(
            HardwareObject target,
            string featureService,
            IEnumerable<string>? attributeNames)
        {
            var feature = RequireFeatureService(target, featureService);
            var requested = Requested(attributeNames);
            if (requested.Length > 0)
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(Invoke(feature, "GetAttribute", name), CultureInfo.InvariantCulture)
                }).ToList();
            return GetEngineeringAttributeInfos(feature, null);
        }

        public object? GetFeatureAttribute(HardwareObject target, string featureService, string name) =>
            Invoke(RequireFeatureService(target, featureService), "GetAttribute", name);

        public void SetFeatureAttributes(HardwareObject target, string featureService, IEnumerable<KeyValuePair<string, object>> values) =>
            Invoke(RequireFeatureService(target, featureService), "SetAttributes", values);

        public IReadOnlyList<HardwareAttributeInfo> GetNetworkItemAttributeInfos(
            HardwareObject target,
            string collectionName,
            int index,
            IEnumerable<string>? attributeNames) =>
            GetEngineeringAttributeInfos(RequireNetworkItem(target, collectionName, index), attributeNames);

        public object? GetNetworkItemAttribute(HardwareObject target, string collectionName, int index, string name) =>
            Invoke(RequireNetworkItem(target, collectionName, index), "GetAttribute", name);

        public void SetNetworkItemAttributes(
            HardwareObject target,
            string collectionName,
            int index,
            IEnumerable<KeyValuePair<string, object>> values) =>
            Invoke(RequireNetworkItem(target, collectionName, index), "SetAttributes", values);

        public byte[]? GetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, int length)
        {
            var value = Invoke(RequireFeatureService(target, "GsdDeviceItem"), "GetPrmData", datasetNumber, byteOffset, length);
            return value is IEnumerable<byte> bytes ? bytes.ToArray() : null;
        }

        public void SetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, IEnumerable<byte> data) =>
            Invoke(RequireFeatureService(target, "GsdDeviceItem"), "SetPrmData", datasetNumber, byteOffset, data);

        public object RequireNetworkItem(HardwareObject target, string collectionName, int index)
        {
            var values = Enumerate(RequireFeatureService(target, "NetworkInterface"), collectionName).ToList();
            if (index < 0 || index >= values.Count)
                throw new InvalidOperationException($"Network interface collection '{collectionName}' has no item at index {index}.");
            return values[index];
        }

        internal HardwareIoAddressInfo ToIoAddressInfo(
            object address,
            HardwareObjectRef reference,
            int index,
            IEnumerable<string>? attributeNames)
        {
            var requested = Requested(attributeNames);
            var names = requested.Length > 0 ? requested : GetEngineeringAttributeNames(address).ToArray();
            return new HardwareIoAddressInfo
            {
                Target = reference,
                Index = index,
                Attributes = names.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = TryGetEngineeringAttribute(address, name)
                }).ToList()
            };
        }

        internal List<HardwareAttributeInfo> GetSelectedEngineeringAttributeInfos(object? target, IEnumerable<string> attributeNames) =>
            ProjectSelectedEngineeringAttributeInfos(target, attributeNames);

        internal static List<HardwareAttributeInfo> ProjectSelectedEngineeringAttributeInfos(object? target, IEnumerable<string> attributeNames)
        {
            if (target == null) return new List<HardwareAttributeInfo>();
            var result = new List<HardwareAttributeInfo>();
            foreach (var name in attributeNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var value = TryGetEngineeringAttribute(target, name);
                if (value != null) result.Add(new HardwareAttributeInfo { Name = name, Value = value });
            }
            return result;
        }

        private object RequireFeatureService(HardwareObject target, string serviceType) =>
            GetFeatureService(target, serviceType)
            ?? throw new InvalidOperationException($"Hardware item does not provide the {serviceType} feature service.");

        private static IReadOnlyList<HardwareAttributeInfo> GetEngineeringAttributeInfos(
            object target,
            IEnumerable<string>? attributeNames)
        {
            var requested = Requested(attributeNames);
            if (requested.Length > 0)
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(Invoke(target, "GetAttribute", name), CultureInfo.InvariantCulture)
                }).ToList();
            var infos = Invoke(target, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null) return Array.Empty<HardwareAttributeInfo>();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(new HardwareAttributeInfo { Name = name!, IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool? });
            }
            return result;
        }

        private static IEnumerable<string> GetEngineeringAttributeNames(object target)
        {
            var infos = Invoke(target, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null) yield break;
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) yield return name!;
            }
        }

        private static string? TryGetEngineeringAttribute(object target, string name)
        {
            try { return Convert.ToString(Invoke(target, "GetAttribute", name), CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static string? TryGetAttribute(HardwareObject target, string name)
        {
            try { return Convert.ToString(target.GetAttribute(name), CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static string[] Requested(IEnumerable<string>? names) =>
            names?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();

        private static IEnumerable<object> Enumerate(object target, string propertyName) =>
            TiaReflectionHelper.GetEnumerableProperty(target, propertyName) ?? Enumerable.Empty<object>();

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
