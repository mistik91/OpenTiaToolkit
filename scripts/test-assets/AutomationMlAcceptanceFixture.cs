using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

public static class AutomationMlAcceptanceFixture
{
    private const string Cpu = "OrderNumber:6ES7 511-1AK02-0AB0/V2.9";
    private const string Et200Sp = "OrderNumber:6ES7 155-6AU02-0BN0/V6.4";
    private const string Et200Mp = "OrderNumber:6ES7 155-5AA01-0AB0/V4.1";
    private const string SpInput = "OrderNumber:6ES7 131-6BF01-0BA0/V0.0";
    private const string MpInput = "OrderNumber:6ES7 521-1BL00-0AB0/V1.0";

    public static void Create(string directory, bool multiplePlcs)
    {
        using (var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface))
        {
            var output = new DirectoryInfo(directory);
            var project = portal.Projects.Create(output, "AutomationMlFixture");
            try
            {
                var seed = AddController(project, "SeedController", "Seed", "192.168.10.1");
                seed.Software.TagTableGroup.TagTables.Single(table => table.IsDefault)
                    .Tags.Create("PreservedMemory", "Bool", "%M100.0");

                if (multiplePlcs)
                {
                    var companion = AddController(project, "CompanionController", "Companion", "192.168.11.1");
                    AddRemote(project, companion, Et200Sp, "CompanionRemote", "192.168.11.2", SpInput, "CompanionInputs", 1);
                    companion.Software.TagTableGroup.TagTables.Single(table => table.IsDefault)
                        .Tags.Create("CompanionReady", "Bool", "%I20.0");
                }

                var cax = project.GetService<CaxProvider>();
                Export(cax, project, directory, "Baseline.aml");
                project.Save();
                project.Archive(output, "Baseline.zap21", ProjectArchivationMode.Compressed);

                AddRemote(project, seed, multiplePlcs ? Et200Mp : Et200Sp, "RemoteStation", "192.168.10.2",
                    multiplePlcs ? MpInput : SpInput, "RemoteInputs", multiplePlcs ? 2 : 1);
                seed.Software.TagTableGroup.TagTables.Single(table => table.IsDefault)
                    .Tags.Create("RemoteReady", "Bool", "%I20.0");
                Export(cax, project, directory, "Desired.aml");
            }
            finally
            {
                project.Close();
            }
        }
    }

    private static Controller AddController(Project project, string name, string prefix, string ip)
    {
        var device = project.Devices.CreateWithItem(Cpu, prefix + "CPU", name);
        var interfaceFeature = Interfaces(device).First(feature => feature.IoControllers.Count > 0);
        var subnet = project.Subnets.Create("System:Subnet.Ethernet", prefix + "PN");
        var node = interfaceFeature.Nodes.Single();
        node.ConnectToSubnet(subnet);
        node.SetAttribute("Address", ip);

        var software = Items(device.DeviceItems).Distinct()
            .Select(item => item.GetService<SoftwareContainer>())
            .Where(container => container != null).Select(container => container.Software).OfType<PlcSoftware>().Single();
        return new Controller(subnet, interfaceFeature.IoControllers[0].CreateIoSystem(prefix + "IO"), software);
    }

    private static void AddRemote(Project project, Controller controller, string stationType, string name,
        string ip, string moduleType, string moduleName, int slot)
    {
        var device = project.Devices.CreateWithItem(stationType, name + "Head", name);
        var interfaceFeature = Interfaces(device).First(feature => feature.IoConnectors.Count > 0 && feature.Nodes.Count > 0);
        var node = interfaceFeature.Nodes.Single();
        node.ConnectToSubnet(controller.Subnet);
        node.SetAttribute("Address", ip);
        interfaceFeature.IoConnectors[0].ConnectToIoSystem(controller.IoSystem);

        var rack = device.DeviceItems.Single(item => item.PositionNumber == 0 && IsRack(item.TypeIdentifier));
        var module = rack.PlugNew(moduleType, moduleName, slot);
        var input = Items(new[] { module }).SelectMany(item => item.Addresses).Distinct()
            .Single(address => address.IoType.ToString() == "Input");
        input.StartAddress = 20;
    }

    private static bool IsRack(string identifier)
    {
        if (identifier == null) return false;
        return identifier.StartsWith("System:Rack.", StringComparison.OrdinalIgnoreCase) ||
            identifier.Replace(" ", string.Empty).StartsWith("OrderNumber:6ES7590-", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<NetworkInterface> Interfaces(Device device)
    {
        return Items(device.DeviceItems).Distinct().Select(item => item.GetService<NetworkInterface>())
            .OfType<NetworkInterface>();
    }

    private static IEnumerable<DeviceItem> Items(IEnumerable<DeviceItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Items(item.DeviceItems))
                yield return child;
        }
    }

    private static void Export(CaxProvider cax, Project project, string directory, string fileName)
    {
        var result = cax.Export(project, new FileInfo(Path.Combine(directory, fileName)));
        if (result.ErrorCount != 0)
            throw new InvalidOperationException("CAx export failed for " + fileName + " (" + result.ErrorCount + " errors).");
    }

    private sealed class Controller
    {
        public Controller(Subnet subnet, IoSystem ioSystem, PlcSoftware software)
        {
            Subnet = subnet;
            IoSystem = ioSystem;
            Software = software;
        }

        public Subnet Subnet { get; private set; }
        public IoSystem IoSystem { get; private set; }
        public PlcSoftware Software { get; private set; }
    }
}
