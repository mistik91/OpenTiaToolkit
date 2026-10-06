namespace OpenTiaToolkit.Core.Services.V21
{
    /// <summary>
    /// Creates a focused V21 component graph for direct compatibility construction.
    /// Production hosts register the individual components in dependency injection.
    /// </summary>
    internal sealed class V21ComponentGraph
    {
        private V21ComponentGraph()
        {
            Types = new V21TypeServiceResolver();
            Objects = new V21EngineeringObjectAccess();
            Software = new V21SoftwareDiscovery(Types);
            Tags = new V21PlcTagStructureAccess(Objects);
            Compilation = new V21CompilationAccess(Types);
            Online = new V21OnlineVerificationAccess(Types);
            Documents = new V21DocumentAccess(Types);
        }

        internal V21TypeServiceResolver Types { get; }
        internal V21SoftwareDiscovery Software { get; }
        internal V21EngineeringObjectAccess Objects { get; }
        internal V21PlcTagStructureAccess Tags { get; }
        internal V21CompilationAccess Compilation { get; }
        internal V21OnlineVerificationAccess Online { get; }
        internal V21DocumentAccess Documents { get; }

        internal static V21ComponentGraph CreateDefault() => new V21ComponentGraph();
    }
}
