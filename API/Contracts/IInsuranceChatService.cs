using API.Models;

namespace API.Contracts;

/// <summary>
/// 多輪對話服務介面。
/// </summary>
public interface IInsuranceChatService
{
    /// <summary>
    /// 進行多輪對話並取得 AI 回覆。
    /// </summary>
    Task<SimpleChatResponse> ChatAsync(SimpleChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重設指定工作階段的對話紀錄。
    /// </summary>
    bool ResetSession(string sessionId);

    /// <summary>
    /// 以既有對話內容覆蓋指定工作階段的對話記憶。
    /// </summary>
    void HydrateSession(string sessionId, IEnumerable<string> historyEntries);
}
