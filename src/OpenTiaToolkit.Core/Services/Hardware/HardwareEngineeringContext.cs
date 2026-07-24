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

        public ProjectBase RequireProject() =>
            _sessionContext.Project ?? throw new InvalidOperationException("No TIA Portal project is open.");

        public TiaPortal RequirePortal() =>
            _sessionContext.Portal ?? throw new InvalidOperationException("Not connected to TIA Portal.");
    }
}
