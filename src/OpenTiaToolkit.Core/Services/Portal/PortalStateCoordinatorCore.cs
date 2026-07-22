using System;

namespace OpenTiaToolkit.Core.Services
{
    internal interface IPortalStateStore<TPortal, TProject, TSession>
        where TPortal : class
        where TProject : class
        where TSession : class
    {
        TPortal? Portal { get; set; }
        int? AttachedProcessId { get; set; }
        bool IsPortalVisible { get; set; }
        TProject? Project { get; set; }
        TSession? Session { get; set; }
        bool OwnsProject { get; set; }
        bool OwnsSession { get; set; }
        void ClearPortalSelection();
        void ClearProjectSelection();
    }

    /// <summary>
    /// Siemens-free atomic state transition engine. Resource cleanup happens only after
    /// the replacement state is committed, and cleanup failures never roll it back.
    /// </summary>
    internal sealed class PortalStateCoordinatorCore<TPortal, TProject, TSession>
        where TPortal : class
        where TProject : class
        where TSession : class
    {
        private readonly IPortalStateStore<TPortal, TProject, TSession> _store;
        private readonly Action<TSession> _closeSession;
        private readonly Action<TProject> _closeProject;
        private readonly Action<TPortal> _disposePortal;
        private readonly Action<Exception, string>? _cleanupWarning;

        public PortalStateCoordinatorCore(
            IPortalStateStore<TPortal, TProject, TSession> store,
            Action<TSession> closeSession,
            Action<TProject> closeProject,
            Action<TPortal> disposePortal,
            Action<Exception, string>? cleanupWarning = null)
        {
            _store = store;
            _closeSession = closeSession;
            _closeProject = closeProject;
            _disposePortal = disposePortal;
            _cleanupWarning = cleanupWarning;
        }

        public void CommitPortal(TPortal portal, int? processId, TSession? session, TProject? project, bool ownsProject, bool ownsSession, bool visible)
        {
            var previous = Capture();
            _store.ClearPortalSelection();
            _store.Portal = portal;
            _store.AttachedProcessId = processId;
            _store.IsPortalVisible = visible;
            _store.Session = session;
            _store.Project = project;
            _store.OwnsProject = ownsProject;
            _store.OwnsSession = ownsSession;
            Release(previous, portal, session, project);
        }

        public void CommitProject(TSession? session, TProject project, bool ownsProject, bool ownsSession)
        {
            var previous = Capture();
            _store.ClearProjectSelection();
            _store.Session = session;
            _store.Project = project;
            _store.OwnsProject = ownsProject;
            _store.OwnsSession = ownsSession;
            Release(previous, _store.Portal, session, project);
        }

        public void Dispose()
        {
            try
            {
                if (_store.OwnsSession && _store.Session != null) _closeSession(_store.Session);
                else if (_store.OwnsProject && _store.Project != null) _closeProject(_store.Project);
            }
            catch (Exception ex) { Warn(ex, "close owned project/session during dispose"); }

            try
            {
                if (_store.Portal != null) _disposePortal(_store.Portal);
            }
            catch (Exception ex) { Warn(ex, "dispose portal during dispose"); }
            finally { _store.ClearPortalSelection(); }
        }

        private Snapshot Capture() => new Snapshot(_store.Portal, _store.Project, _store.Session, _store.OwnsProject, _store.OwnsSession);

        private void Release(Snapshot previous, TPortal? retainedPortal, TSession? retainedSession, TProject? retainedProject)
        {
            try
            {
                if (previous.OwnsSession && previous.Session != null && !ReferenceEquals(previous.Session, retainedSession)) _closeSession(previous.Session);
                else if (previous.OwnsProject && previous.Project != null && !ReferenceEquals(previous.Project, retainedProject)) _closeProject(previous.Project);
            }
            catch (Exception ex) { Warn(ex, "close previous owned project/session"); }

            if (previous.Portal == null || ReferenceEquals(previous.Portal, retainedPortal)) return;
            try { _disposePortal(previous.Portal); }
            catch (Exception ex) { Warn(ex, "dispose previous portal attachment"); }
        }

        private void Warn(Exception exception, string operation) => _cleanupWarning?.Invoke(exception, operation);

        private sealed class Snapshot
        {
            public Snapshot(TPortal? portal, TProject? project, TSession? session, bool ownsProject, bool ownsSession)
            { Portal = portal; Project = project; Session = session; OwnsProject = ownsProject; OwnsSession = ownsSession; }
            public TPortal? Portal { get; }
            public TProject? Project { get; }
            public TSession? Session { get; }
            public bool OwnsProject { get; }
            public bool OwnsSession { get; }
        }
    }
}
