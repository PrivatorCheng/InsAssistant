namespace API.Contracts;

using API.Models;

/// <summary>
/// 查詢結果裝飾服務。
/// </summary>
public interface IResultDecorationService
{
    /// <summary>
    /// 對查詢結果進行裝飾轉換。
    /// </summary>
    /// <param name="rows">查詢結果行列表</param>
    /// <param name="targetTables">目標資料表清單</param>
    /// <param name="dataFields">資料欄位清單</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>裝飾後的行列表</returns>
    Task<List<Dictionary<string, object?>>> DecorateResultsAsync(
        List<Dictionary<string, object?>> rows,
        List<string> targetTables,
        List<DimensionDetail> dataFields,
        CancellationToken cancellationToken);
}
