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
using Microsoft.Extensions.Logging;

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

        [TestMethod]
        public async Task LocalSession_IsOpenedAndCommittedWithToolkitOwnership()
        {
            var runtime = new FakeRuntime();
            runtime.LocalSessionResult = runtime.LocalSession;
            var state = new RecordingState();

            Assert.IsTrue(await Create(ConnectedContext(runtime.CurrentPortal), runtime, state)
                .OpenProjectAsync(@"C:\Sessions\Line1.als21"));

            Assert.AreEqual(1, runtime.LocalSessionOpenCalls);
            Assert.AreSame(runtime.LocalSession, state.Session);
            Assert.AreSame(runtime.Project, state.Project);
            Assert.IsTrue(state.OwnsSession);
            Assert.IsTrue(state.OwnsProject);
        }

        private static PortalProjectOpenWorkflow Create(EngineeringSessionContext context, FakeRuntime runtime, RecordingState state, PortalOperationStatus? status = null) =>
            new PortalProjectOpenWorkflow(context, null, state, runtime, status ?? new PortalOperationStatus());

        [TestMethod]
        public async Task LocalSession_OpenFailure_IdentifiesNativeStepAndRetainsFullExceptionForLogging()
        {
            var failure = new InvalidOperationException("outer diagnostic", new ObjectDisposedException("LocalSession"));
            var runtime = new FakeRuntime { LocalSessionOpenFailure = failure };
            var state = new RecordingState();
            var status = new PortalOperationStatus();
            var logger = new RecordingLogger();
            var workflow = new PortalProjectOpenWorkflow(ConnectedContext(), logger, state, runtime, status);
            Assert.IsFalse(await workflow.OpenProjectAsync(@"C:\Sessions\Line1.als21"));
            StringAssert.Contains(status.LastError, "open-local-session");
            StringAssert.Contains(status.LastError, "LocalSession");
            Assert.AreSame(failure, logger.Error);
            Assert.AreSame(failure.InnerException, logger.Error!.InnerException);
            Assert.AreEqual(0, runtime.SessionProjectReads);
            Assert.AreEqual(0, state.ProjectCommits);
            Assert.AreEqual(1, runtime.LocalSessionOpenCalls);
        }

        [TestMethod]
        public async Task LocalSession_ProjectReadFailure_DoesNotSelectOrRetryDisposedCandidate()
        {
            var runtime = new FakeRuntime
            {
                SessionProjectFailure = new ObjectDisposedException("LocalSession")
            };
            runtime.LocalSessionResult = runtime.LocalSession;
            var state = new RecordingState();
            var status = new PortalOperationStatus();
            Assert.IsFalse(await Create(ConnectedContext(), runtime, state, status).OpenProjectAsync(@"C:\Sessions\Line1.als21"));
            StringAssert.Contains(status.LastError, "read-local-session-project");
            Assert.AreEqual(1, runtime.SessionProjectReads);
            Assert.AreEqual(1, runtime.LocalSessionOpenCalls);
            Assert.AreEqual(0, state.ProjectCommits);
        }

        [TestMethod]
        public async Task LocalSession_AttachedPortal_PreservesUserOwnership()
        {
            var runtime = new FakeRuntime();
            runtime.LocalSessionResult = runtime.LocalSession;
            var context = ConnectedContext(runtime.CurrentPortal);
            context.AttachedPortalProcessId = 11;
            var state = new RecordingState();
            Assert.IsTrue(await Create(context, runtime, state).OpenProjectAsync(@"C:\Sessions\Line1.als21"));
            Assert.IsFalse(state.OwnsSession);
            Assert.IsFalse(state.OwnsProject);
        }

        private sealed class RecordingLogger : ILogger
        {
            public Exception? Error { get; private set; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (exception != null) Error = exception; }
        }

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
            public LocalSession? Session { get; private set; }
            public bool OwnsProject { get; private set; }
            public bool OwnsSession { get; private set; }
            public void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible)
            { PortalCommits++; Portal = portal; ProcessId = processId; Project = project; Session = session; OwnsProject = ownsProject; OwnsSession = ownsSession; }
            public void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession)
            { ProjectCommits++; Project = project; Session = session; OwnsProject = ownsProject; OwnsSession = ownsSession; }
            public void ClearPortalSelection() { }
            public void ClearProjectSelection() { }
            public void Dispose() { }
        }

        private sealed class FakeRuntime : IPortalRuntimeAdapter
        {
            public TiaPortal CurrentPortal { get; } = Uninitialized<TiaPortal>();
            public TiaPortal AttachedPortal { get; } = Uninitialized<TiaPortal>();
            public TiaPortal HiddenPortal { get; } = Uninitialized<TiaPortal>();
            public Project Project { get; } = Uninitialized<Project>();
            public Project OtherProject { get; } = Uninitialized<Project>();
            public LocalSession LocalSession { get; } = Uninitialized<LocalSession>();
            public List<PortalProcessDescriptor> Processes { get; } = new List<PortalProcessDescriptor>();
            public List<PortalOpenProject> CurrentProjects { get; } = new List<PortalOpenProject>();
            public List<PortalOpenProject> AttachedProjects { get; } = new List<PortalOpenProject>();
            public PortalOpenProject? SelectedIdentity { get; set; }
            public PortalOpenProject? OpenResult { get; set; }
            public LocalSession? LocalSessionResult { get; set; }
            public int StartCalls { get; private set; }
            public int OpenCalls { get; private set; }
            public int LocalSessionOpenCalls { get; private set; }
            public int SessionProjectReads { get; private set; }
            public Exception? LocalSessionOpenFailure { get; set; }
            public Exception? SessionProjectFailure { get; set; }
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
            public LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file)
            { LocalSessionOpenCalls++; if (LocalSessionOpenFailure != null) throw LocalSessionOpenFailure; return LocalSessionResult; }
            public ProjectBase? GetSessionProject(LocalSession session)
            { SessionProjectReads++; if (SessionProjectFailure != null) throw SessionProjectFailure; return ReferenceEquals(session, LocalSession) ? Project : null; }
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
