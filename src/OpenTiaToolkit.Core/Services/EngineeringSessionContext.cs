using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Multiuser;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Holds the currently attached TIA Portal, project, session, and PLC selection state.
    /// This is intentionally kept as a small shared context so feature services can evolve
    /// independently without duplicating ownership logic.
    /// </summary>
    internal sealed class EngineeringSessionContext
    {
        private readonly SemaphoreSlim _opennessGate = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<int> _opennessGateDepth = new AsyncLocal<int>();

        public TiaPortal? Portal { get; set; }

        public int? AttachedPortalProcessId { get; set; }

        public ProjectBase? Project { get; set; }

        public LocalSession? Session { get; set; }

        public bool OwnsCurrentProject { get; set; }

        public bool OwnsCurrentSession { get; set; }

        public object? PlcSoftware { get; set; }

        public Device? CurrentPlcDevice { get; set; }

        public DeviceItem? CurrentPlcDeviceItem { get; set; }

        public string? CurrentPlcName { get; set; }

        public HmiSoftware? HmiSoftware { get; set; }

        public Device? CurrentHmiDevice { get; set; }

        public string? CurrentHmiName { get; set; }

        public void ClearPortalSelection()
        {
            Portal = null;
            AttachedPortalProcessId = null;
            ClearProjectSelection();
        }

        public void ClearProjectSelection()
        {
            Project = null;
            Session = null;
            OwnsCurrentProject = false;
            OwnsCurrentSession = false;
            ClearPlcSelection();
            ClearHmiSelection();
        }

        public void ClearPlcSelection()
        {
            PlcSoftware = null;
            CurrentPlcDevice = null;
            CurrentPlcDeviceItem = null;
            CurrentPlcName = null;
        }

        public void ClearHmiSelection()
        {
            HmiSoftware = null;
            CurrentHmiDevice = null;
            CurrentHmiName = null;
        }

        public async Task RunExclusiveAsync(Func<Task> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            await RunExclusiveAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_opennessGateDepth.Value > 0)
            {
                return await action().ConfigureAwait(false);
            }

            await _opennessGate.WaitAsync().ConfigureAwait(false);
            _opennessGateDepth.Value++;
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                _opennessGateDepth.Value--;
                _opennessGate.Release();
            }
        }

        public T RunExclusive<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_opennessGateDepth.Value > 0)
            {
                return action();
            }

            _opennessGate.Wait();
            _opennessGateDepth.Value++;
            try
            {
                return action();
            }
            finally
            {
                _opennessGateDepth.Value--;
                _opennessGate.Release();
            }
        }
    }
}
