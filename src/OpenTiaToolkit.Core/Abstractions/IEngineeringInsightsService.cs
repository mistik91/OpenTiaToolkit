using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Abstractions;

public interface IEngineeringInsightsService
{
    Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedAlarmsAsync();
    Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedRuntimeSettingsAsync();
    Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedTextListsAsync();
    Task<EngineeringValidationResult> ValidateSafetyAsync();
    Task<EngineeringValidationResult> InspectMultiuserAsync();
    Task<EngineeringValidationResult> InspectProjectSecurityAsync();
    Task<IReadOnlyList<EngineeringInsightItem>> GetWatchTableDetailsAsync();
    Task<IReadOnlyList<EngineeringInsightItem>> GetTechnologyObjectDetailsAsync();
    Task<IReadOnlyList<EngineeringInsightItem>> GetPlcAlarmTextListDetailsAsync();
}
