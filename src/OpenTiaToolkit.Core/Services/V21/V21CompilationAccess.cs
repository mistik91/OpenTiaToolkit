using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Download;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System.Linq.Expressions;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal sealed class V21CompilationAccess
    {
        private readonly V21TypeServiceResolver _resolver;
        public V21CompilationAccess(V21TypeServiceResolver resolver)
        {
            _resolver = resolver;
        }
        public V21CompileResult? Compile(object plcSoftware)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            if (plcSoftware is not PlcSoftware typedPlcSoftware)
            {
                return null;
            }

            var compileService = typedPlcSoftware.GetService<ICompilable>();
            if (compileService == null)
            {
                return null;
            }

            var rawResult = compileService.Compile();

            return new V21CompileResult(
                rawResult.ErrorCount,
                rawResult.WarningCount,
                rawResult.State.ToString(),
                null,
                ExtractCompileDiagnostics(rawResult));
        }

        private static IReadOnlyList<CompilationDiagnostic> ExtractCompileDiagnostics(CompilerResult rawResult)
        {
            return V21CompilationDiagnosticProjector.Project(
                rawResult.Messages,
                message => message.State.ToString(),
                message => message.Path,
                message => message.Description,
                message => message.Messages);
        }
    }
}
