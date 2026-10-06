// Fixture-only connection discovery. Not exposed to benchmarked models.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Download;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

internal static class Ott35SimulatorFixture
{
    private const string ApiRoot = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    private const string ProjectPath = @"C:\OpenTiaBench\Gold\20261002-ott35-preflight\OTT_Benchmark\OTT_Benchmark.ap21";

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string path = Path.Combine(ApiRoot, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Inspect(args); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int Inspect(string[] args)
    {
        try
        {
            int pid;
            if (args.Length != 2 || args[0] != "--inspect" || !int.TryParse(args[1], out pid) || pid <= 0)
                throw new ArgumentException("Usage: Ott35SimulatorFixture --inspect <exact TIA PID>");
            using (var portal = TiaPortal.GetProcesses().Single(item => item.Id == pid).Attach())
            {
                if (portal.Projects.Count != 1) throw new InvalidOperationException("Exactly one disposable project is required.");
                var project = portal.Projects.First();
                if (!string.Equals(project.Path.FullName, ProjectPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing non-fixture project: " + project.Path.FullName);
                var items = project.Devices.SelectMany(device => Walk(device.DeviceItems)).Where(item =>
                {
                    var software = ((IEngineeringServiceProvider)item).GetService<SoftwareContainer>();
                    return software != null && software.Software != null && software.Software.Name == "PLC_Main";
                }).ToArray();
                if (items.Length != 1) throw new InvalidOperationException("PLC_Main must resolve to one device item.");
                var provider = ((IEngineeringServiceProvider)items[0]).GetService<DownloadProvider>();
                if (provider == null) throw new InvalidOperationException("No download provider.");
                Console.WriteLine("Configured=" + provider.Configuration.IsConfigured);
                foreach (var mode in provider.Configuration.Modes)
                    foreach (var pc in mode.PcInterfaces)
                    {
                        Console.WriteLine("Mode=" + mode.Name + " PC=" + pc.Name + " Number=" + pc.Number);
                        foreach (var target in pc.TargetInterfaces)
                        {
                            Console.WriteLine("  Target=" + target.Name);
                            foreach (var address in target.Addresses) Console.WriteLine("    Address=" + address.Address + " Name=" + address.Name);
                        }
                        foreach (var address in pc.Addresses) Console.WriteLine("  PCAddress=" + address.Address + " Name=" + address.Name);
                    }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static System.Collections.Generic.IEnumerable<DeviceItem> Walk(DeviceItemComposition items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Walk(item.DeviceItems)) yield return child;
        }
    }
}
