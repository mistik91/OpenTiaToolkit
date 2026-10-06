using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static OpenTiaToolkit.Core.Services.PortalProjectStateInspector;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class PortalProcessDescriptor
    {
        internal PortalProcessDescriptor(int id, string? version, TiaPortalProcess? process = null) { Id = id; Version = version; Process = process; }
        public int Id { get; }
        public string? Version { get; }
        internal TiaPortalProcess? Process { get; }
    }

    internal sealed class PortalOpenProject
    {
        public PortalOpenProject(LocalSession? session, ProjectBase project, string? name = null, string? path = null)
        {
            Session = session;
            Project = project;
            Name = name ?? project.Name;
            Path = path ?? TryGetProjectPath(project);
        }
        public LocalSession? Session { get; }
        public ProjectBase Project { get; }
        public string Name { get; }
        public string? Path { get; }
    }

    internal interface IPortalRuntimeAdapter
    {
        IReadOnlyList<PortalProcessDescriptor> GetProcesses();
        TiaPortal Attach(PortalProcessDescriptor process);
        TiaPortal Start(TiaPortalMode mode);
        IReadOnlyList<PortalOpenProject> GetOpenProjects(TiaPortal portal);
        PortalOpenProject GetProjectIdentity(ProjectBase project);
        LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file);
        ProjectBase? GetSessionProject(LocalSession session);
        PortalOpenProject? OpenProjectWithUpgrade(TiaPortal portal, FileInfo file, UmacDelegate? authentication);
        PortalOpenProject? CreateProject(TiaPortal portal, DirectoryInfo directory, string name);
        PortalOpenProject? RetrieveProject(TiaPortal portal, FileInfo archive, DirectoryInfo target, bool upgrade, UmacDelegate? authentication);
        Device CreateDevice(Project project, string typeIdentifier, string name);
        void ArchiveProject(Project project, DirectoryInfo directory, string name, ProjectArchivationMode mode);
        void SaveProject(Project project);
        void SaveSession(LocalSession session);
        void SaveProjectAs(Project project, DirectoryInfo directory);
        void CloseProject(Project project);
        void CloseSession(LocalSession session);
        void DisposePortal(TiaPortal portal);
    }

    /// <summary>The bounded V21 edge for portal discovery and raw Siemens resource operations.</summary>
    internal sealed class PortalRuntimeAdapter : IPortalRuntimeAdapter
    {
        public IReadOnlyList<PortalProcessDescriptor> GetProcesses() => TiaPortal.GetProcesses()
            .Select(process => new PortalProcessDescriptor(process.Id, GetVersion(process), process)).ToList();

        public TiaPortal Attach(PortalProcessDescriptor process) =>
            (process.Process ?? throw new InvalidOperationException($"TIA Portal process {process.Id} is not attachable.")).Attach();

        public TiaPortal Start(TiaPortalMode mode) => new TiaPortal(mode);

        public IReadOnlyList<PortalOpenProject> GetOpenProjects(TiaPortal portal)
        {
            var result = new List<PortalOpenProject>();
            if (portal.LocalSessions != null)
                result.AddRange(portal.LocalSessions.Where(session => session.Project != null).Select(session => new PortalOpenProject(session, session.Project)));
            if (portal.Projects != null)
                result.AddRange(portal.Projects.Select(project => new PortalOpenProject(null, project)));
            return result;
        }

        public PortalOpenProject GetProjectIdentity(ProjectBase project) => new PortalOpenProject(null, project);

        public LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file) => portal.LocalSessions.Open(file);
        public ProjectBase? GetSessionProject(LocalSession session) => session.Project;
        public PortalOpenProject? OpenProjectWithUpgrade(TiaPortal portal, FileInfo file, UmacDelegate? authentication)
        {
            var project = authentication == null ? portal.Projects.OpenWithUpgrade(file) : portal.Projects.OpenWithUpgrade(file, authentication);
            return project == null ? null : new PortalOpenProject(null, project);
        }
        public PortalOpenProject? CreateProject(TiaPortal portal, DirectoryInfo directory, string name)
        {
            var project = portal.Projects.Create(directory, name);
            return project == null ? null : new PortalOpenProject(null, project);
        }
        public PortalOpenProject? RetrieveProject(TiaPortal portal, FileInfo archive, DirectoryInfo target, bool upgrade, UmacDelegate? authentication)
        {
            var project = upgrade
                ? (authentication == null ? portal.Projects.RetrieveWithUpgrade(archive, target) : portal.Projects.RetrieveWithUpgrade(archive, target, authentication))
                : (authentication == null ? portal.Projects.Retrieve(archive, target) : portal.Projects.Retrieve(archive, target, authentication));
            return project == null ? null : new PortalOpenProject(null, project);
        }
        public Device CreateDevice(Project project, string typeIdentifier, string name) => project.Devices.CreateWithItem(typeIdentifier, name, name);
        public void ArchiveProject(Project project, DirectoryInfo directory, string name, ProjectArchivationMode mode) => project.Archive(directory, name, mode);
        public void SaveProject(Project project) => project.Save();
        public void SaveSession(LocalSession session) => session.Save();
        public void SaveProjectAs(Project project, DirectoryInfo directory) => project.SaveAs(directory);
        public void CloseProject(Project project) => project.Close();
        public void CloseSession(LocalSession session) => session.Close();
        public void DisposePortal(TiaPortal portal) => portal.Dispose();

        private static string? GetVersion(TiaPortalProcess process)
        {
            try
            {
                var product = process.InstalledSoftware?.FirstOrDefault(candidate => candidate.Name?.IndexOf("Automation Portal", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? process.InstalledSoftware?.FirstOrDefault();
                var version = product?.Version;
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
            catch { return null; }
        }
    }

    internal sealed class PortalOperationStatus
    {
        public string? LastError { get; set; }
    }
}
