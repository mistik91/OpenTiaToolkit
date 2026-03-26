using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Holds the currently attached TIA Portal, project, session, and PLC selection state.
    /// This is intentionally kept as a small shared context so feature services can evolve
    /// independently without duplicating ownership logic.
    /// </summary>
    internal sealed class EngineeringSessionContext
    {
        public TiaPortal? Portal { get; set; }

        public ProjectBase? Project { get; set; }

        public LocalSession? Session { get; set; }

        public bool OwnsCurrentProject { get; set; }

        public bool OwnsCurrentSession { get; set; }

        public object? PlcSoftware { get; set; }

        public Device? CurrentPlcDevice { get; set; }

        public string? CurrentPlcName { get; set; }

        public void ClearPortalSelection()
        {
            Portal = null;
            ClearProjectSelection();
        }

        public void ClearProjectSelection()
        {
            Project = null;
            Session = null;
            OwnsCurrentProject = false;
            OwnsCurrentSession = false;
            ClearPlcSelection();
        }

        public void ClearPlcSelection()
        {
            PlcSoftware = null;
            CurrentPlcDevice = null;
            CurrentPlcName = null;
        }
    }
}
