using System.Text.Json.Serialization;

namespace API.Models;

/// <summary>
/// 待上傳文件項目。
/// </summary>
public sealed class TodoDocumentItem
{
    /// <summary>
    /// 文件名稱。
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// 是否已上傳。
    /// </summary>
    [JsonPropertyName("uploaded")]
    public required bool Uploaded { get; set; }
}