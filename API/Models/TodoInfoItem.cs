using System.Text.Json.Serialization;

namespace API.Models;

/// <summary>
/// 待確認資訊項目。
/// </summary>
public sealed class TodoInfoItem
{
    /// <summary>
    /// 資訊名稱。
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// 資訊內容。
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }
}