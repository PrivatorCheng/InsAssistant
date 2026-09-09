namespace API.Models;

/// <summary>
/// 基礎響應資料模型(汎型)。
/// </summary>
public class ResponseDataSchema<T>
{
    /// <summary>
    /// 響應代碼。
    /// </summary>
    public required int Code { get; set; }

    /// <summary>
    /// 響應訊息。
    /// </summary>
    public required string Msg { get; set; }

    /// <summary>
    /// 響應資料。
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// 響應詳細訊息。
    /// </summary>
    public IDictionary<string, string[]>? Details { get; set; }

    /// <summary>
    /// 追蹤識別碼。
    /// </summary>
    public required string TraceId { get; set; }
}
