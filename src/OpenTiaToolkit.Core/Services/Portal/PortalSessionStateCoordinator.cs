using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using System;
using System.Diagnostics;

namespace OpenTiaToolkit.Core.Services
{
    internal interface IPortalSessionStateCoordinator
    {
        void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible);
        void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession);
        void ClearPortalSelection();
        void ClearProjectSelection();
        void Dispose();
    }

    /// <summary>Adapts the Siemens-backed engineering context to the atomic state transition core.</summary>
    internal sealed class PortalSessionStateCoordinator : IPortalSessionStateCoordinator
    {
        private readonly PortalStateCoordinatorCore<TiaPortal, ProjectBase, LocalSession> _core;

        public PortalSessionStateCoordinator(EngineeringSessionContext context, ILogger? logger, IPortalRuntimeAdapter runtime)
        {
            _core = new PortalStateCoordinatorCore<TiaPortal, ProjectBase, LocalSession>(
                new EngineeringSessionStateStore(context),
                runtime.CloseSession,
                project => { if (project is Project ownedProject) runtime.CloseProject(ownedProject); },
                runtime.DisposePortal,
                (exception, operation) =>
                {
                    logger?.LogWarning(exception, "Failed to {CleanupOperation} after a portal state transition.", operation);
                    Trace.TraceWarning($"[PortalSessionStateCoordinator] Failed to {operation}: {PortalSessionPolicy.FormatExceptionMessage(exception)}");
                });
        }

        public void CommitPortal(TiaPortal portal, int? processId, LocalSession? session, ProjectBase? project, bool ownsProject, bool ownsSession, bool isPortalVisible) =>
            _core.CommitPortal(portal, processId, session, project, ownsProject, ownsSession, isPortalVisible);

        public void CommitProject(LocalSession? session, ProjectBase project, bool ownsProject, bool ownsSession) =>
            _core.CommitProject(session, project, ownsProject, ownsSession);

        public void ClearPortalSelection() => _core.ClearPortalSelection();

        public void ClearProjectSelection() => _core.ClearProjectSelection();

        public void Dispose() => _core.Dispose();

        private sealed class EngineeringSessionStateStore : IPortalStateStore<TiaPortal, ProjectBase, LocalSession>
        {
            private readonly EngineeringSessionContext _context;
            public EngineeringSessionStateStore(EngineeringSessionContext context) => _context = context;
            public TiaPortal? Portal { get => _context.Portal; set => _context.Portal = value; }
            public int? AttachedProcessId { get => _context.AttachedPortalProcessId; set => _context.AttachedPortalProcessId = value; }
            public bool IsPortalVisible { get => _context.IsPortalVisible; set => _context.IsPortalVisible = value; }
            public ProjectBase? Project { get => _context.Project; set => _context.Project = value; }
            public LocalSession? Session { get => _context.Session; set => _context.Session = value; }
            public bool OwnsProject { get => _context.OwnsCurrentProject; set => _context.OwnsCurrentProject = value; }
            public bool OwnsSession { get => _context.OwnsCurrentSession; set => _context.OwnsCurrentSession = value; }
            public void ClearPortalSelection() => _context.ClearPortalSelection();
            public void ClearProjectSelection() => _context.ClearProjectSelection();
        }
    }
}
