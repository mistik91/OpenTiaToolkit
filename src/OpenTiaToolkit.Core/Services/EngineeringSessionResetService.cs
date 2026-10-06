using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class EngineeringSessionResetService : ISessionResetService
    {
        private readonly EngineeringSessionContext _sessionContext;

        public EngineeringSessionResetService(EngineeringSessionContext sessionContext)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        }

        public Task<SessionResetResult> ResetAsync()
        {
            return Task.FromResult(_sessionContext.ResetWithoutGate());
        }
    }
}
