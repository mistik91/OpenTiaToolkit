using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.Hardware;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Commands.Hardware
{
    internal static class HardwarePatchCommandValidation
    {
        public static bool TryValidate(
            CommandArgs args,
            bool dryRun,
            out IReadOnlyList<HardwarePatchOperation> operations,
            out CommandResult? failure)
        {
            operations = HardwareCommandSupport.RequireOperations(args);
            var issues = HardwarePatchStructuralValidator.Validate(operations);
            if (issues.Count == 0)
            {
                failure = null;
                return true;
            }

            var blocked = HardwarePatchStructuralValidator.CreateBlockedResult(
                operations,
                dryRun,
                args.Has("transaction-description") ? args.GetString("transaction-description") : null,
                issues);
            failure = CommandResult.CreateFailure(blocked.SafetyMessage!, blocked);
            return false;
        }
    }
}
