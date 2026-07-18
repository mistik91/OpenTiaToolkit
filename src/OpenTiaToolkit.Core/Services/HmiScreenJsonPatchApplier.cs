using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPatchApplyResult
    {
        public HmiScreenJsonPatchApplyResult(HmiJsonScreen screen, List<HmiScreenJsonPatchOperationResult> operationResults)
        {
            Screen = screen;
            OperationResults = operationResults;
        }

        public HmiJsonScreen Screen { get; }

        public List<HmiScreenJsonPatchOperationResult> OperationResults { get; }

        public int FailedCount => OperationResults.Count(result => !result.Succeeded);
    }

    internal static class HmiScreenJsonPatchApplier
    {
        private static readonly HmiScreenJsonPatchEngine Engine = HmiScreenJsonPatchEngine.CreateDefault();
        private static readonly HmiScreenJsonPatchFilterPlanner FilterPlanner = new HmiScreenJsonPatchFilterPlanner();

        public static HmiScreenJsonPatchApplyResult Apply(
            HmiJsonScreen screen,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            return Engine.Apply(screen, operations);
        }

        public static HmiJsonFilterOptions MergeOperationFilters(
            HmiJsonFilterOptions? filters,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            return FilterPlanner.MergeOperationFilters(filters, operations);
        }

        public static List<HmiScreenJsonPatchOperationResult> ValidateOperationFilters(
            HmiJsonFilterOptions filters,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            return FilterPlanner.ValidateOperationFilters(filters, operations);
        }
    }
}
