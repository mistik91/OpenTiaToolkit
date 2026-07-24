using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PortalInstanceWorkflowTests
    {
        [TestMethod]
        public async Task ConnectToInstance_AttachesExactRequestedProcessId()
        {
            var runtime = new FakeRuntime(11, 22);
            var state = new RecordingState();
            var workflow = Create(runtime, state, new EngineeringSessionContext());

            Assert.IsTrue(await workflow.ConnectToInstanceAsync(22));

            Assert.AreEqual(22, runtime.AttachedProcessId);
            Assert.AreEqual(22, state.ProcessId);
        }

        [TestMethod]
        public async Task CandidateInspectionFailure_DisposesCandidateAndRetainsExistingState()
        {
            var context = new EngineeringSessionContext { AttachedPortalProcessId = 7, CurrentPlcName = "PLC_OLD", CurrentHmiName = "HMI_OLD" };
            var runtime = new FakeRuntime(22) { InspectionException = new InvalidOperationException("inspection failed") };
            var state = new RecordingState();
            var workflow = Create(runtime, state, context);

            Assert.IsFalse(await workflow.ConnectToInstanceAsync(22));

            Assert.IsTrue(runtime.CandidateDisposed);
            Assert.AreEqual(0, state.PortalCommits);
            Assert.AreEqual(7, context.AttachedPortalProcessId);
            Assert.AreEqual("PLC_OLD", context.CurrentPlcName);
            Assert.AreEqual("HMI_OLD", context.CurrentHmiName);
        }

        [TestMethod]
        public async Task Connection_WithOneOpenProject_SelectsIt()
        {
            var runtime = new FakeRuntime(22);
            runtime.OpenProjects.Add(new PortalOpenProject(null, Uninitialized<Project>(), "P1", @"C:\P1.ap21"));
            var state = new RecordingState();

            Assert.IsTrue(await Create(runtime, state, new EngineeringSessionContext()).ConnectToInstanceAsync(22));

            Assert.IsNotNull(state.Project);
        }

        [TestMethod]
        public async Task Connection_WithMultipleOpenProjectsLeavesProjectUnselected()
        {
            var runtime = new FakeRuntime(22);
            runtime.OpenProjects.Add(new PortalOpenProject(null, Uninitialized<Project>(), "P1", @"C:\P1.ap21"));
            runtime.OpenProjects.Add(new PortalOpenProject(null, Uninitialized<Project>(), "P2", @"C:\P2.ap21"));
            var state = new RecordingState();

            Assert.IsTrue(await Create(runtime, state, new EngineeringSessionContext()).ConnectToInstanceAsync(22));

            Assert.IsNull(state.Project);
        }

        [TestMethod]
        public async Task Connect_WithNoRunningProcesses_StartsHiddenPortal()
        {
            var runtime = new FakeRuntime();
            var state = new RecordingState();

            Assert.IsTrue(await Create(runtime, state, new EngineeringSessionContext()).ConnectAsync());

            Assert.AreEqual(TiaPortalMode.WithoutUserInterface, runtime.StartedMode);
            Assert.IsFalse(state.IsPortalVisible);
            Assert.IsNull(state.ProcessId);
        }

        [TestMethod]
        public async Task GetInstances_ReportsTheCurrentlyAttachedProcess()
        {
            var runtime = new FakeRuntime(11, 22);
            var context = new EngineeringSessionContext { AttachedPortalProcessId = 22 };

            var instances = await Create(runtime, new RecordingState(), context).GetInstancesAsync();

            Assert.IsFalse(instances.Single(instance => instance.ProcessId == 11).IsAttached);
            Assert.IsTrue(instances.Single(instance => instance.ProcessId == 22).IsAttached);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task PortalRelease_ClearsSelectionEvenWhenDisposalFails(bool close)
        {
            var runtime = new FakeRuntime { DisposalException = new InvalidOperationException("dispose failed") };
            var context = new EngineeringSessionContext
            {
                Portal = runtime.Portal,
                AttachedPortalProcessId = 22,
                CurrentPlcName = "PLC_OLD",
                CurrentHmiName = "HMI_OLD"
            };
            var state = new PortalSessionStateCoordinator(context, null, runtime);
            var workflow = Create(runtime, state, context);

            var result = close
                ? await workflow.CloseTiaPortalAsync()
                : await workflow.DisconnectAsync();

            Assert.IsFalse(result);
            Assert.IsNull(context.Portal);
            Assert.IsNull(context.AttachedPortalProcessId);
            Assert.IsNull(context.CurrentPlcName);
            Assert.IsNull(context.CurrentHmiName);
        }

        private static PortalInstanceWorkflow Create(FakeRuntime runtime, IPortalSessionStateCoordinator state, EngineeringSessionContext context) =>
            new PortalInstanceWorkflow(context, null, state, runtime, new PortalOperationStatus());

        private static T Uninitialized<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));

        private sealed class RecordingState : IPortalSessionStateCoordinator
        {
            public int PortalCommits { get; private set; }
            public int? ProcessId { get; private set; }
            public ProjectBase? Project { get; private set; }
            public bool IsPortalVisible { get; private set; }
            public void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible)
            { PortalCommits++; ProcessId = processId; Project = project; IsPortalVisible = isPortalVisible; }
            public void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession) => Project = project;
            public void ClearPortalSelection() { }
            public void ClearProjectSelection() { }
            public void Dispose() { }
        }

        private sealed class FakeRuntime : IPortalRuntimeAdapter
        {
            private readonly TiaPortal _portal = Uninitialized<TiaPortal>();
            public TiaPortal Portal => _portal;
            public FakeRuntime(params int[] processIds)
            {
                foreach (int id in processIds) Processes.Add(new PortalProcessDescriptor(id, "V21"));
            }
            public List<PortalProcessDescriptor> Processes { get; } = new List<PortalProcessDescriptor>();
            public List<PortalOpenProject> OpenProjects { get; } = new List<PortalOpenProject>();
            public Exception? InspectionException { get; set; }
            public Exception? DisposalException { get; set; }
            public int? AttachedProcessId { get; private set; }
            public TiaPortalMode? StartedMode { get; private set; }
            public bool CandidateDisposed { get; private set; }
            public IReadOnlyList<PortalProcessDescriptor> GetProcesses() => Processes;
            public TiaPortal Attach(PortalProcessDescriptor process) { AttachedProcessId = process.Id; return _portal; }
            public TiaPortal Start(TiaPortalMode mode) { StartedMode = mode; return _portal; }
            public IReadOnlyList<PortalOpenProject> GetOpenProjects(TiaPortal portal) { if (InspectionException != null) throw InspectionException; return OpenProjects; }
            public PortalOpenProject GetProjectIdentity(ProjectBase project) => throw new NotSupportedException();
            public void DisposePortal(TiaPortal portal)
            {
                CandidateDisposed = true;
                if (DisposalException != null) throw DisposalException;
            }
            public LocalSession? OpenLocalSession(TiaPortal portal, FileInfo file) => throw new NotSupportedException();
            public ProjectBase? GetSessionProject(LocalSession session) => throw new NotSupportedException();
            public PortalOpenProject? OpenProjectWithUpgrade(TiaPortal portal, FileInfo file, UmacDelegate? authentication) => throw new NotSupportedException();
            public PortalOpenProject? CreateProject(TiaPortal portal, DirectoryInfo directory, string name) => throw new NotSupportedException();
            public PortalOpenProject? RetrieveProject(TiaPortal portal, FileInfo archive, DirectoryInfo target, bool upgrade, UmacDelegate? authentication) => throw new NotSupportedException();
            public Device CreateDevice(Project project, string typeIdentifier, string name) => throw new NotSupportedException();
            public void ArchiveProject(Project project, DirectoryInfo directory, string name, ProjectArchivationMode mode) => throw new NotSupportedException();
            public void SaveProject(Project project) => throw new NotSupportedException();
            public void SaveSession(LocalSession session) => throw new NotSupportedException();
            public void SaveProjectAs(Project project, DirectoryInfo directory) => throw new NotSupportedException();
            public void CloseProject(Project project) => throw new NotSupportedException();
            public void CloseSession(LocalSession session) => throw new NotSupportedException();
        }
    }
}
