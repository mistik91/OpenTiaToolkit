using Siemens.Engineering;
using System;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal sealed class HardwareEngineeringContext
    {
        private readonly EngineeringSessionContext _sessionContext;

        public HardwareEngineeringContext(EngineeringSessionContext sessionContext)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        }

        public EngineeringSessionContext Session => _sessionContext;

        public ProjectBase RequireProject()
        {
            _sessionContext.ThrowIfProjectUnavailable();
            var project = _sessionContext.Project ?? throw new InvalidOperationException("No TIA Portal project is open.");
            try { _ = project.Name; return project; }
            catch (Exception ex) { throw _sessionContext.RecordReadFailure(ex); }
        }

        public TiaPortal RequirePortal() =>
            _sessionContext.Portal ?? throw new InvalidOperationException("Not connected to TIA Portal.");
    }
}
