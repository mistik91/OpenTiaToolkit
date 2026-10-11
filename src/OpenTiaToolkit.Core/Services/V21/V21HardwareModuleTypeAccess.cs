using Siemens.Engineering;
using System;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal static class V21HardwareModuleTypeAccess
    {
        public static string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier)
        {
            var utilities = TiaReflectionHelper.GetProperty(project, "HwUtilities")
                ?? throw new InvalidOperationException("The current project does not expose hardware utilities.");
            var find = utilities.GetType().GetMethod("Find", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Hardware utilities do not expose Find(string).");
            var provider = Invoke(find, utilities, "ModuleInformationProvider")
                ?? throw new InvalidOperationException("The current project has no module information provider.");
            return ReadModuleTypes(provider, partialTypeIdentifier);
        }

        internal static string[] ReadModuleTypes(object provider, string partialTypeIdentifier)
        {
            var method = provider.GetType().GetMethod("FindModuleTypes", new[] { typeof(string) })
                ?? throw new InvalidOperationException("The module information provider does not expose FindModuleTypes(string).");
            return Invoke(method, provider, partialTypeIdentifier ?? string.Empty) as string[] ?? Array.Empty<string>();
        }

        private static object? Invoke(MethodInfo method, object target, string argument)
        {
            try { return method.Invoke(target, new object[] { argument }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
