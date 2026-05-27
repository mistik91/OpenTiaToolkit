using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Isolates V21 feature-service and invocation details used by generic hardware workflows.
    /// </summary>
    internal sealed class V21HardwareAdapter
    {
        private readonly V21OpennessAdapter _adapter;

        public V21HardwareAdapter(V21OpennessAdapter? adapter = null)
        {
            _adapter = adapter ?? new V21OpennessAdapter();
        }

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

        public object? GetFeatureService(HardwareObject target, string serviceType)
        {
            return _adapter.GetService(target, serviceType);
        }

        public IReadOnlyList<HardwareAttributeInfo> GetAttributeInfos(HardwareObject target, IEnumerable<string>? attributeNames)
        {
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = TryGetAttribute(target, name)
                }).ToList();
            }

            var infos = target.GetAttributeInfos();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new HardwareAttributeInfo
                {
                    Name = name!,
                    IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                });
            }

            return result;
        }

        public string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier)
        {
            var utilities = TiaReflectionHelper.GetProperty(project, "HwUtilities")
                ?? throw new InvalidOperationException("The current project does not expose hardware utilities.");
            var provider = Invoke(utilities, "Find", "ModuleInformationProvider");
            if (provider == null)
            {
                return Array.Empty<string>();
            }

            var method = provider.GetType().GetMethod("FindModuleTypes", new[] { typeof(string) });
            return method?.Invoke(provider, new object[] { partialTypeIdentifier ?? string.Empty }) as string[] ?? Array.Empty<string>();
        }

        public object? GetAttribute(HardwareObject target, string name)
        {
            return target.GetAttribute(name);
        }

        public IReadOnlyList<HardwareAttributeInfo> GetFeatureAttributeInfos(HardwareObject target, string featureService, IEnumerable<string>? attributeNames)
        {
            var feature = RequireFeatureService(target, featureService);
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(GetFeatureAttribute(feature, name), CultureInfo.InvariantCulture)
                }).ToList();
            }

            var infos = Invoke(feature, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null)
            {
                return Array.Empty<HardwareAttributeInfo>();
            }

            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new HardwareAttributeInfo
                    {
                        Name = name!,
                        IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                    });
                }
            }
            return result;
        }

        public object? GetFeatureAttribute(HardwareObject target, string featureService, string name)
        {
            return GetFeatureAttribute(RequireFeatureService(target, featureService), name);
        }

        public void SetFeatureAttributes(HardwareObject target, string featureService, IEnumerable<KeyValuePair<string, object>> values)
        {
            Invoke(RequireFeatureService(target, featureService), "SetAttributes", values);
        }

        public IReadOnlyList<HardwareAttributeInfo> GetNetworkItemAttributeInfos(HardwareObject target, string collectionName, int index, IEnumerable<string>? attributeNames)
        {
            return GetEngineeringAttributeInfos(RequireNetworkItem(target, collectionName, index), attributeNames);
        }

        public object? GetNetworkItemAttribute(HardwareObject target, string collectionName, int index, string name)
        {
            return Invoke(RequireNetworkItem(target, collectionName, index), "GetAttribute", name);
        }

        public void SetNetworkItemAttributes(HardwareObject target, string collectionName, int index, IEnumerable<KeyValuePair<string, object>> values)
        {
            Invoke(RequireNetworkItem(target, collectionName, index), "SetAttributes", values);
        }

        public byte[]? GetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, int length)
        {
            var service = RequireFeatureService(target, "GsdDeviceItem");
            var value = Invoke(service, "GetPrmData", datasetNumber, byteOffset, length);
            return value is IEnumerable<byte> bytes ? bytes.ToArray() : null;
        }

        public void SetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, IEnumerable<byte> data)
        {
            var service = RequireFeatureService(target, "GsdDeviceItem");
            Invoke(service, "SetPrmData", datasetNumber, byteOffset, data);
        }

        public HardwareNetworkInfo? GetNetworkInfo(HardwareObject target, HardwareObjectRef reference)
        {
            var network = GetFeatureService(target, "NetworkInterface");
            if (network == null)
            {
                return null;
            }

            var info = new HardwareNetworkInfo { Target = reference };
            int index = 0;
            foreach (var node in Enumerate(network, "Nodes"))
            {
                var subnet = TiaReflectionHelper.GetProperty(node, "ConnectedSubnet");
                info.Nodes.Add(new HardwareNodeInfo
                {
                    Name = TiaReflectionHelper.GetProperty(node, "Name")?.ToString(),
                    NodeType = TiaReflectionHelper.GetProperty(node, "NodeType")?.ToString(),
                    ConnectedSubnet = TiaReflectionHelper.GetProperty(subnet, "Name")?.ToString()
                });
                index++;
            }

            index = 0;
            foreach (var controller in Enumerate(network, "IoControllers"))
            {
                var ioSystem = TiaReflectionHelper.GetProperty(controller, "IoSystem");
                info.IoControllers.Add(new HardwareIoControllerInfo
                {
                    Index = index++,
                    IoSystemName = TiaReflectionHelper.GetProperty(ioSystem, "Name")?.ToString()
                });
            }

            index = 0;
            foreach (var connector in Enumerate(network, "IoConnectors"))
            {
                var ioSystem = TiaReflectionHelper.GetProperty(connector, "ConnectedToIoSystem");
                info.IoConnectors.Add(new HardwareIoConnectorInfo
                {
                    Index = index++,
                    ConnectedIoSystemName = TiaReflectionHelper.GetProperty(ioSystem, "Name")?.ToString()
                });
            }

            return info;
        }

        public object RequireNetworkItem(HardwareObject target, string collectionName, int index)
        {
            var network = RequireFeatureService(target, "NetworkInterface");
            var values = Enumerate(network, collectionName).ToList();
            if (index < 0 || index >= values.Count)
            {
                throw new InvalidOperationException($"Network interface collection '{collectionName}' has no item at index {index}.");
            }

            return values[index];
        }

        public void ConnectNodeToSubnet(object node, Subnet subnet)
        {
            Invoke(node, "ConnectToSubnet", subnet);
        }

        public void DisconnectNode(object node)
        {
            Invoke(node, "DisconnectFromSubnet");
        }

        public object CreateIoSystem(object controller, string ioSystemName)
        {
            return Invoke(controller, "CreateIoSystem", ioSystemName)
                ?? throw new InvalidOperationException("Creating the IO system returned no object.");
        }

        public void ConnectIoConnector(object connector, object ioSystem)
        {
            Invoke(connector, "ConnectToIoSystem", ioSystem);
        }

        public void DisconnectIoConnector(object connector)
        {
            Invoke(connector, "DisconnectFromIoSystem");
        }

        private object RequireFeatureService(HardwareObject target, string serviceType)
        {
            return GetFeatureService(target, serviceType)
                ?? throw new InvalidOperationException($"Hardware item does not provide the {serviceType} feature service.");
        }

        private static object? GetFeatureAttribute(object feature, string name)
        {
            return Invoke(feature, "GetAttribute", name);
        }

        private static IReadOnlyList<HardwareAttributeInfo> GetEngineeringAttributeInfos(object target, IEnumerable<string>? attributeNames)
        {
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(Invoke(target, "GetAttribute", name), CultureInfo.InvariantCulture)
                }).ToList();
            }

            var infos = Invoke(target, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null) return Array.Empty<HardwareAttributeInfo>();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new HardwareAttributeInfo
                    {
                        Name = name!,
                        IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                    });
                }
            }
            return result;
        }

        private static IEnumerable<object> Enumerate(object target, string propertyName)
        {
            return TiaReflectionHelper.GetEnumerableProperty(target, propertyName) ?? Enumerable.Empty<object>();
        }

        private static string? TryGetAttribute(HardwareObject target, string name)
        {
            try
            {
                return Convert.ToString(target.GetAttribute(name), CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static object? Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName &&
                    candidate.GetParameters().Length == args.Length);
            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' is not available on {target.GetType().Name}.");
            }

            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
