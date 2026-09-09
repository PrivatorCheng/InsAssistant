namespace API.Models;

/// <summary>
/// 保單清單項目。
/// </summary>
public sealed class PolicyListItem
{
    /// <summary>
    /// 保單號碼。
    /// </summary>
    public required string PolicyNo { get; set; }

    /// <summary>
    /// 被保險人姓名。
    /// </summary>
    public required string InsuredName { get; set; }

    /// <summary>
    /// 車牌號碼。
    /// </summary>
    public required string PlateNo { get; set; }

    /// <summary>
    /// 投保險種清單。
    /// </summary>
    public required List<string> InsureTypeList { get; set; }
}
