using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Resets the shared TIA engineering session without using the serialized Openness gate.
    /// </summary>
    public interface ISessionResetService
    {
        Task<SessionResetResult> ResetAsync();
    }

    public sealed class SessionResetResult
    {
        public SessionResetResult(bool hadPortal, bool hadProject, bool hadPlcSelection, bool hadHmiSelection, string? warning = null)
        {
            HadPortal = hadPortal;
            HadProject = hadProject;
            HadPlcSelection = hadPlcSelection;
            HadHmiSelection = hadHmiSelection;
            Warning = warning;
        }

        public bool HadPortal { get; }

        public bool HadProject { get; }

        public bool HadPlcSelection { get; }

        public bool HadHmiSelection { get; }

        public string? Warning { get; }
    }
}
