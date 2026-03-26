namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Exposes the shared engineering session context inside the core service layer.
    /// </summary>
    internal interface IEngineeringSessionContextAccessor
    {
        EngineeringSessionContext SessionContext { get; }
    }
}
