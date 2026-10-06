// Fixture construction only. Never used by benchmarked models or production workflows.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

internal static class Program
{
    private const string ApiRoot = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
        {
            string candidate = Path.Combine(ApiRoot, new AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        try
        {
            if (args.Length == 1 && args[0] == "--describe")
            {
                Console.WriteLine("Fixture-only builder; exact disposable target; source library read-only; no project save.");
                Console.WriteLine(FixtureBuilder.ProjectPath);
                return 0;
            }
            if (args.Length != 2 || args[0] != "--build")
                throw new ArgumentException("Usage: Ott32LibraryFixtureBuilder --describe | --build <TIA PID>");
            int portalPid;
            if (!int.TryParse(args[1], out portalPid) || portalPid <= 0)
                throw new ArgumentException("An exact positive campaign TIA PID is required.");
            FixtureBuilder.Build(portalPid);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Do not save/archive a failed construction. Close this disposable copy without saving.");
            return 1;
        }
    }
}

internal static class FixtureBuilder
{
    // Deliberately fixed: supplying another PID cannot authorize another project.
    internal const string ProjectPath = @"C:\OpenTiaBench\Live\20260930-library-gold-02\OTT_Benchmark\OTT_Benchmark.ap21";
    private const string SourceLibraryPath = @"E:\Documents\Automation\TestLibrary\TestLibrary.al21";

    internal static void Build(int portalPid)
    {
        var process = TiaPortal.GetProcesses().Single(item => item.Id == portalPid);
        using (var portal = process.Attach())
        {
            if (portal.Projects.Count != 1)
                throw new InvalidOperationException("Exactly one disposable project must be open.");
            var project = portal.Projects.First();
            if (!string.Equals(project.Path.FullName, ProjectPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing a non-fixture project: " + project.Path.FullName);

            var sourceLibrary = portal.GlobalLibraries.FirstOrDefault(item =>
                string.Equals(item.Path.FullName, SourceLibraryPath, StringComparison.OrdinalIgnoreCase));
            if (sourceLibrary == null)
                sourceLibrary = portal.GlobalLibraries.Open(new FileInfo(SourceLibraryPath), OpenMode.ReadOnly);
            if (!sourceLibrary.IsReadOnly || sourceLibrary.IsModified)
                throw new InvalidOperationException("Source library must be open read-only and unmodified.");

            var plc = FindPlc(project);
            var approved = RequireBlock(plc, "OTT31_ProjectSource");
            var legacy = RequireBlock(plc, "OTT31_WorkspaceTarget");
            var existing = RequireBlock(plc, "OTT31_Distractor");
            var masterRoot = project.ProjectLibrary.MasterCopyFolder;
            if (masterRoot.MasterCopies.Count != 0 || masterRoot.Folders.Count != 0 ||
                project.ProjectLibrary.TypeFolder.Types.Count != 0 || project.ProjectLibrary.TypeFolder.Folders.Count != 0 ||
                plc.BlockGroup.Groups.Find("OTT32_Reuse") != null)
                throw new InvalidOperationException("Fixture construction requires an empty project library and no reuse group.");

            var hmiFolder = sourceLibrary.MasterCopyFolder.Folders.Find("Design_2");
            var hmiSource = hmiFolder == null ? null : hmiFolder.MasterCopies.Find("D2_Control_1");
            var udtFolder = sourceLibrary.TypeFolder.Folders.Find("UDT");
            var type = udtFolder == null ? null : udtFolder.Types.Find("MyANALOG_LEVEL");
            if (hmiSource == null || type == null)
                throw new InvalidOperationException("Required read-only source library assets were not found.");
            var oldVersion = type.Versions.Single(item => item.VersionNumber.ToString() == "0.0.3");
            if (!(oldVersion is PlcTypeLibraryTypeVersion))
                throw new InvalidOperationException("The selected old library version is not a PLC data type.");

            // All context/source checks precede the first write. This is an unsaved disposable seed.
            approved.SetAttribute("Name", "OTT32_ApprovedOffset");
            legacy.SetAttribute("Name", "OTT32_ApprovedOffset_Legacy");
            existing.SetAttribute("Name", "OTT32_ExistingOffset");
            var standards = masterRoot.Folders.Create("OTT32_Standards");
            var approvedRoot = standards.Folders.Create("Approved");
            var approvedPlc = approvedRoot.Folders.Create("PLC");
            var approvedHmi = approvedRoot.Folders.Create("HMI");
            var legacyPlc = standards.Folders.Create("Legacy").Folders.Create("PLC");
            var approvedMaster = approvedPlc.MasterCopies.Create((IMasterCopySource)approved);
            approvedMaster.SetAttribute("Name", "Offset");
            var legacyMaster = legacyPlc.MasterCopies.Create((IMasterCopySource)legacy);
            legacyMaster.SetAttribute("Name", "Offset");
            var hmiMaster = approvedHmi.MasterCopies.CreateFrom(hmiSource);
            hmiMaster.SetAttribute("Name", "Offset");

            // Materialize an older instance, then refresh only the project-library definitions.
            // The held-out task must report the outdated instance, never upgrade it.
            var oldInstance = plc.TypeGroup.Types.CreateFrom((PlcTypeLibraryTypeVersion)oldVersion);
            type.UpdateLibrary(project.ProjectLibrary);
            plc.BlockGroup.Groups.Create("OTT32_Reuse");
            approved.Delete();

            if (sourceLibrary.IsModified)
                throw new InvalidOperationException("Read-only source library unexpectedly changed.");
            Console.WriteLine("Prepared three same-leaf master copies, an older PLC type instance, and an empty reuse group.");
            Console.WriteLine("Existing type instance: " + oldInstance.Name);
            Console.WriteLine("No project save was performed. Independently compile/read back before saving and archiving.");
        }
    }

    private static PlcBlock RequireBlock(PlcSoftware plc, string name)
    {
        var block = plc.BlockGroup.Blocks.Find(name);
        if (block == null)
            throw new InvalidOperationException("Required immutable VCI seed block missing: " + name);
        return block;
    }

    private static PlcSoftware FindPlc(Project project)
    {
        var device = project.Devices.Single(item => item.Name == "PLC_Main");
        foreach (var item in device.DeviceItems)
        {
            var software = FindPlc(item);
            if (software != null) return software;
        }
        throw new InvalidOperationException("PLC_Main software could not be resolved.");
    }

    private static PlcSoftware FindPlc(DeviceItem item)
    {
        var container = item.GetService<SoftwareContainer>();
        if (container != null && container.Software is PlcSoftware)
            return (PlcSoftware)container.Software;
        foreach (var child in item.DeviceItems)
        {
            var software = FindPlc(child);
            if (software != null) return software;
        }
        return null;
    }
}
