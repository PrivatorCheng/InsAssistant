using API.Models;

namespace API.Contracts;

public interface IQueryPlanningService
{
    /// <summary>
    /// Step3 規劃流程。實作者必須於回傳結果中填入完整 LLM 呼叫紀錄（Step3PlanningResult.LlmRecord）。
    /// </summary>
    Task<Step3PlanningResult> PlanAsync(
        PreprocessResult preprocessResult,
        List<string> selectedLayouts,
        FormulaSpec? previousFormulaSpec,
    string entityListText,
        CancellationToken cancellationToken = default);
}
