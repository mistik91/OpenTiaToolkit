using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PortalProjectOpenWorkflowTests
    {
        private const string Requested = @"C:\Projects\Line1.ap21";

        [TestMethod]
        public async Task ExactSelectedProject_IsReusedWithoutOpening()
        {
            var runtime = new FakeRuntime();
            var context = ConnectedContext();
            context.Project = runtime.Project;
            runtime.SelectedIdentity = Candidate(runtime.Project, "Line1", Requested);

            Assert.IsTrue(await Create(context, runtime, new RecordingState()).OpenProjectAsync(Requested));
            Assert.AreEqual(0, runtime.OpenCalls);
        }

        [TestMethod]
        public async Task ExactProjectInCurrentPortal_IsSelectedWithoutOpening()
        {
            var runtime = new FakeRuntime();
            runtime.CurrentProjects.Add(Candidate(runtime.Project, "Line1", Requested));
            var state = new RecordingState();

            Assert.IsTrue(await Create(ConnectedContext(runtime.CurrentPortal), runtime, state).OpenProjectAsync(Requested));
            Assert.AreSame(runtime.Project, state.Project);
            Assert.AreEqual(0, runtime.OpenCalls);
        }

        [TestMethod]
        public async Task ExactProjectInAnotherPortal_IsAttachedAndReused()
        {
            var runtime = new FakeRuntime();
            runtime.Processes.Add(new PortalProcessDescriptor(22, "V21"));
            runtime.AttachedProjects.Add(Candidate(runtime.Project, "Line1", Requested));
            var state = new RecordingState();
            var context = ConnectedContext(runtime.CurrentPortal);
            context.AttachedPortalProcessId = 11;

            Assert.IsTrue(await Create(context, runtime, state).OpenProjectAsync(Requested));
            Assert.AreEqual(22, state.ProcessId);
            Assert.AreSame(runtime.Project, state.Project);
        }

        [TestMethod]
        public async Task DifferentOpenProject_IsRejectedWithoutDedicatedInstance()
        {
            var runtime = new FakeRuntime();
            runtime.CurrentProjects.Add(Candidate(runtime.OtherProject, "Line1", @"C:\Other\Line1.ap21"));
            var status = new PortalOperationStatus();

            Assert.IsFalse(await Create(ConnectedContext(runtime.CurrentPortal), runtime, new RecordingState(), status).OpenProjectAsync(Requested));
            StringAssert.Contains(status.LastError, "different project open");
            Assert.AreEqual(0, runtime.StartCalls);
        }

        [TestMethod]
        public async Task ExplicitNewInstance_UsesHiddenPortalAndCommitsExactCandidate()
        {
            var runtime = new FakeRuntime();
            runtime.CurrentProjects.Add(Candidate(runtime.OtherProject, "Other", @"C:\Other\Other.ap21"));
            runtime.OpenResult = Candidate(runtime.Project, "Line1", Requested);
            var state = new RecordingState();

            Assert.IsTrue(await Create(ConnectedContext(runtime.CurrentPortal), runtime, state).OpenProjectAsync(Requested, null, true));
            Assert.AreEqual(1, runtime.StartCalls);
            Assert.AreSame(runtime.HiddenPortal, state.Portal);
            Assert.IsTrue(state.OwnsProject);
        }

        [TestMethod]
        public async Task MismatchedOpenedProject_IsClosedDisposedAndNeverCommitted()
        {
            var runtime = new FakeRuntime { OpenResult = null };
            runtime.CurrentProjects.Add(Candidate(runtime.OtherProject, "Other", @"C:\Other\Other.ap21"));
            runtime.OpenResult = Candidate(runtime.Project, "Line1", @"C:\Wrong\Line1.ap21");
            var state = new RecordingState();

            Assert.IsFalse(await Create(ConnectedContext(runtime.CurrentPortal), runtime, state).OpenProjectAsync(Requested, null, true));
            Assert.IsTrue(runtime.ProjectClosed);
            Assert.IsTrue(runtime.HiddenPortalDisposed);
            Assert.AreEqual(0, state.PortalCommits);
            Assert.AreEqual(0, state.ProjectCommits);
        }

        private static PortalProjectOpenWorkflow Create(EngineeringSessionContext context, FakeRuntime runtime, RecordingState state, PortalOperationStatus? status = null) =>
            new PortalProjectOpenWorkflow(context, null, state, runtime, status ?? new PortalOperationStatus());

        private static EngineeringSessionContext ConnectedContext(TiaPortal? portal = null) => new EngineeringSessionContext { Portal = portal ?? Uninitialized<TiaPortal>() };
        private static PortalOpenProject Candidate(ProjectBase project, string name, string path) => new PortalOpenProject(null, project, name, path);
        private static T Uninitialized<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));

        private sealed class RecordingState : IPortalSessionStateCoordinator
        {
            public int PortalCommits { get; private set; }
            public int ProjectCommits { get; private set; }
            public TiaPortal? Portal { get; private set; }
            public int? ProcessId { get; private set; }
            public ProjectBase? Project { get; private set; }
            public bool OwnsProject { get; private set; }
            public void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible)
            { PortalCommits++; Portal = portal; ProcessId = processId; Project = project; OwnsProject = ownsProject; }
            public void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession)
            { ProjectCommits++; Project = project; OwnsProject = ownsProject; }
            public void Dispose() { }
        }

        private sealed class FakeRuntime : IPortalRuntimeAdapter
        {
            public TiaPortal CurrentPortal { get; } = Uninitialized<TiaPortal>();
            public TiaPortal AttachedPortal { get; } = Uninitialized<TiaPortal>();
            public TiaPortal HiddenPortal { get; } = Uninitialized<TiaPortal>();
            public Project Project { get; } = Uninitialized<Project>();
            public Project OtherProject { get; } = Uninitialized<Project>();
            public List<PortalProcessDescriptor> Processes { get; } = new List<PortalProcessDescriptor>();
            public List<PortalOpenProject> CurrentProjects { get; } = new List<PortalOpenProject>();
            public List<PortalOpenProject> AttachedProjects { get; } = new List<PortalOpenProject>();
            public PortalOpenProject? SelectedIdentity { get; set; }
            public PortalOpenProject? OpenResult { get; set; }
            public int StartCalls { get; private set; }
            public int OpenCalls { get; private set; }
            public bool ProjectClosed { get; private set; }
            public bool HiddenPortalDisposed { get; private set; }
            public IReadOnlyList<PortalProcessDescriptor> GetProcesses() => Processes;
            public TiaPortal Attach(PortalProcessDescriptor process) => AttachedPortal;
            public TiaPortal Start(TiaPortalMode mode) { StartCalls++; return HiddenPortal; }
            public IReadOnlyList<PortalOpenProject> GetOpenProjects(TiaPortal portal) => ReferenceEquals(portal, AttachedPortal) ? AttachedProjects : CurrentProjects;
            public PortalOpenProject GetProjectIdentity(ProjectBase project) => SelectedIdentity ?? throw new InvalidOperationException("No identity configured");
            public PortalOpenProject? OpenProjectWithUpgrade(TiaPortal portal, FileInfo file, UmacDelegate? authentication) { OpenCalls++; return OpenResult; }
            public void CloseProject(Project project) => ProjectClosed = true;
            public void DisposePortal(TiaPortal portal) { if (ReferenceEquals(portal, HiddenPortal)) HiddenPortalDisposed = true; }
            public LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file) => throw new NotSupportedException();
            public ProjectBase? GetSessionProject(LocalSession session) => throw new NotSupportedException();
            public PortalOpenProject? CreateProject(TiaPortal portal, DirectoryInfo directory, string name) => throw new NotSupportedException();
            public PortalOpenProject? RetrieveProject(TiaPortal portal, FileInfo archive, DirectoryInfo target, bool upgrade, UmacDelegate? authentication) => throw new NotSupportedException();
            public Device CreateDevice(Project project, string typeIdentifier, string name) => throw new NotSupportedException();
            public void ArchiveProject(Project project, DirectoryInfo directory, string name, ProjectArchivationMode mode) => throw new NotSupportedException();
            public void SaveProject(Project project) => throw new NotSupportedException();
            public void SaveSession(LocalSession session) => throw new NotSupportedException();
            public void SaveProjectAs(Project project, DirectoryInfo directory) => throw new NotSupportedException();
            public void CloseSession(LocalSession session) => throw new NotSupportedException();
        }
    }
}
