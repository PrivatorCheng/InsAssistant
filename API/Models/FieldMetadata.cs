namespace API.Models;

/// <summary>
/// 欄位中繼資料。
/// </summary>
public class FieldMetadata
{
    /// <summary>
    /// 欄位名稱。
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 欄位型別。
    /// </summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>
    /// 是否為維度欄位。
    /// </summary>
    public bool IsDimension { get; set; }

    /// <summary>
    /// 是否為量值欄位。
    /// </summary>
    public bool IsMeasure { get; set; }
}
