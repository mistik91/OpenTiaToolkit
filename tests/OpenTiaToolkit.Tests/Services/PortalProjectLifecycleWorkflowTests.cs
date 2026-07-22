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
    public class PortalProjectLifecycleWorkflowTests
    {
        [TestMethod]
        public async Task CloseWithSave_SavesBeforeClosingAndClearsAfterPortalConfirmsClosed()
        {
            var runtime = new FakeRuntime();
            var context = SelectedProject(runtime);

            Assert.IsTrue(await Create(context, runtime).CloseProjectAsync(saveChanges: true));

            CollectionAssert.AreEqual(new[] { "save-project", "close-project" }, runtime.Operations);
            Assert.IsNull(context.Project);
        }

        [TestMethod]
        public async Task Close_WhenPortalStillReportsProjectOpen_RetainsSelection()
        {
            var runtime = new FakeRuntime();
            var context = SelectedProject(runtime);
            runtime.OpenProjects.Add(runtime.ProjectIdentity);

            Assert.IsFalse(await Create(context, runtime).CloseProjectAsync());

            Assert.AreSame(runtime.Project, context.Project);
        }

        [TestMethod]
        public async Task Close_WhenSiemensCloseThrows_ClearsStaleSelection()
        {
            var runtime = new FakeRuntime { CloseException = new InvalidOperationException("close failed") };
            var context = SelectedProject(runtime);

            Assert.IsFalse(await Create(context, runtime).CloseProjectAsync());

            Assert.IsNull(context.Project);
        }

        [TestMethod]
        public async Task Close_WithMultipleUnselectedProjects_ReturnsActionableAmbiguity()
        {
            var runtime = new FakeRuntime();
            runtime.OpenProjects.Add(runtime.ProjectIdentity);
            runtime.OpenProjects.Add(new PortalOpenProject(null, runtime.OtherProject, "Other", @"C:\Projects\Other.ap21"));
            var status = new PortalOperationStatus();
            var context = new EngineeringSessionContext { Portal = runtime.Portal };

            Assert.IsFalse(await Create(context, runtime, status).CloseProjectAsync());
            StringAssert.Contains(status.LastError, "multiple open projects");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Retrieve_ForwardsUpgradeChoiceAndSelectsReturnedProject(bool upgrade)
        {
            var runtime = new FakeRuntime { RetrieveResult = null };
            runtime.RetrieveResult = runtime.ProjectIdentity;
            var state = new RecordingState();
            string target = Path.Combine(Path.GetTempPath(), "OTT-Chapter7-" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = await Create(new EngineeringSessionContext { Portal = runtime.Portal }, runtime, state: state)
                    .RetrieveProjectAsync(@"C:\Archive\Line1.zap21", target, upgrade, null);

                Assert.IsNotNull(result);
                Assert.AreEqual(upgrade, runtime.LastRetrieveUpgrade);
                Assert.AreSame(runtime.Project, state.Project);
            }
            finally
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }

        [TestMethod]
        public async Task Save_UsesLocalSessionWhenSelected()
        {
            var runtime = new FakeRuntime();
            var context = SelectedProject(runtime);
            context.Session = Uninitialized<LocalSession>();

            Assert.IsTrue(await Create(context, runtime).SaveProjectAsync());
            CollectionAssert.AreEqual(new[] { "save-session" }, runtime.Operations);
        }

        [TestMethod]
        public async Task SaveAs_RejectsLocalSessionWithoutCallingSiemensSaveAs()
        {
            var runtime = new FakeRuntime();
            var context = SelectedProject(runtime);
            context.Session = Uninitialized<LocalSession>();

            Assert.IsFalse(await Create(context, runtime).SaveAsProjectAsync(@"C:\Target"));
            Assert.IsFalse(runtime.SaveAsCalled);
        }

        [TestMethod]
        public async Task Archive_ForwardsNormalizedTargetAndModeWithoutImplicitSave()
        {
            var runtime = new FakeRuntime();
            var targetDirectory = Path.Combine(Path.GetTempPath(), "OTT-Chapter7-Archive-" + Guid.NewGuid().ToString("N"));
            var archivePath = Path.Combine(targetDirectory, "Line1.zap21");
            try
            {
                var result = await Create(SelectedProject(runtime), runtime)
                    .ArchiveProjectAsync(archivePath, "compressed");

                Assert.IsNotNull(result);
                Assert.AreEqual(Path.GetFullPath(archivePath), result!.ArchivePath);
                Assert.AreEqual(ProjectArchivationMode.Compressed, runtime.ArchiveMode);
                Assert.AreEqual("Line1.zap21", runtime.ArchiveName);
                Assert.IsFalse(runtime.Operations.Contains("save-project"));
            }
            finally
            {
                if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
            }
        }

        [TestMethod]
        public async Task CreateFailure_InDedicatedCandidatePortalDisposesCandidateAndRetainsSelection()
        {
            var runtime = new FakeRuntime { CreateException = new InvalidOperationException("create failed") };
            runtime.OpenProjects.Add(runtime.ProjectIdentity);
            var context = SelectedProject(runtime);
            var state = new RecordingState();
            string projectPath = Path.Combine(Path.GetTempPath(), "OTT-Chapter7-Create-" + Guid.NewGuid().ToString("N"), "New.ap21");

            var result = await Create(context, runtime, state: state).CreateProjectAsync(projectPath);

            Assert.IsNull(result);
            Assert.IsTrue(runtime.PortalDisposed);
            Assert.AreSame(runtime.Project, context.Project);
            Assert.IsNull(state.Project);
        }

        private static EngineeringSessionContext SelectedProject(FakeRuntime runtime) => new EngineeringSessionContext { Portal = runtime.Portal, Project = runtime.Project };
        private static PortalProjectLifecycleWorkflow Create(EngineeringSessionContext context, FakeRuntime runtime, PortalOperationStatus? status = null, RecordingState? state = null) =>
            new PortalProjectLifecycleWorkflow(context, null, state ?? (IPortalSessionStateCoordinator)new PortalSessionStateCoordinator(context, null, runtime), runtime, status ?? new PortalOperationStatus());
        private static T Uninitialized<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));

        private sealed class RecordingState : IPortalSessionStateCoordinator
        {
            public ProjectBase? Project { get; private set; }
            public void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible) => Project = project;
            public void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession) => Project = project;
            public void ClearPortalSelection() { }
            public void ClearProjectSelection() { }
            public void Dispose() { }
        }

        private sealed class FakeRuntime : IPortalRuntimeAdapter
        {
            public FakeRuntime() => ProjectIdentity = new PortalOpenProject(null, Project, "Line1", @"C:\Projects\Line1.ap21");
            public TiaPortal Portal { get; } = Uninitialized<TiaPortal>();
            public Project Project { get; } = Uninitialized<Project>();
            public Project OtherProject { get; } = Uninitialized<Project>();
            public PortalOpenProject ProjectIdentity { get; }
            public List<PortalOpenProject> OpenProjects { get; } = new List<PortalOpenProject>();
            public List<string> Operations { get; } = new List<string>();
            public PortalOpenProject? RetrieveResult { get; set; }
            public bool? LastRetrieveUpgrade { get; private set; }
            public bool SaveAsCalled { get; private set; }
            public bool PortalDisposed { get; private set; }
            public Exception? CreateException { get; set; }
            public Exception? CloseException { get; set; }
            public string? ArchiveName { get; private set; }
            public ProjectArchivationMode? ArchiveMode { get; private set; }
            public IReadOnlyList<PortalProcessDescriptor> GetProcesses() => Array.Empty<PortalProcessDescriptor>();
            public TiaPortal Attach(PortalProcessDescriptor process) => throw new NotSupportedException();
            public TiaPortal Start(TiaPortalMode mode) => Portal;
            public IReadOnlyList<PortalOpenProject> GetOpenProjects(TiaPortal portal) => OpenProjects;
            public PortalOpenProject GetProjectIdentity(ProjectBase project) => ProjectIdentity;
            public PortalOpenProject? RetrieveProject(TiaPortal portal, FileInfo archive, DirectoryInfo target, bool upgrade, UmacDelegate? authentication) { LastRetrieveUpgrade = upgrade; return RetrieveResult; }
            public void SaveProject(Project project) => Operations.Add("save-project");
            public void SaveSession(LocalSession session) => Operations.Add("save-session");
            public void CloseProject(Project project)
            {
                Operations.Add("close-project");
                if (CloseException != null) throw CloseException;
            }
            public PortalOpenProject? OpenProjectWithUpgrade(TiaPortal portal, FileInfo file, UmacDelegate? authentication) => throw new NotSupportedException();
            public LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file) => throw new NotSupportedException();
            public ProjectBase? GetSessionProject(LocalSession session) => throw new NotSupportedException();
            public PortalOpenProject? CreateProject(TiaPortal portal, DirectoryInfo directory, string name)
            {
                if (CreateException != null) throw CreateException;
                throw new NotSupportedException();
            }
            public Device CreateDevice(Project project, string typeIdentifier, string name) => throw new NotSupportedException();
            public void ArchiveProject(Project project, DirectoryInfo directory, string name, ProjectArchivationMode mode)
            {
                ArchiveName = name;
                ArchiveMode = mode;
                Operations.Add("archive-project");
            }
            public void SaveProjectAs(Project project, DirectoryInfo directory) => SaveAsCalled = true;
            public void CloseSession(LocalSession session) => throw new NotSupportedException();
            public void DisposePortal(TiaPortal portal) => PortalDisposed = true;
        }
    }
}
