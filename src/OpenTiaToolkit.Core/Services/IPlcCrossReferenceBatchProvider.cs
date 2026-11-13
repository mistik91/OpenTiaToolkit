using OpenTiaToolkit.Core.Models;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services;

/// <summary>Creates a lazy target inventory owned by one analysis, never consumer evidence.</summary>
internal interface IPlcCrossReferenceBatchProvider
{
    Func<string, Task<PlcCrossReferenceResult>> CreateCrossReferenceLookup();
}
