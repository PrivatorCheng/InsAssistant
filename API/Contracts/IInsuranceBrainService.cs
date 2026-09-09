namespace API.Contracts;

/// <summary>
/// 車險商品知識庫服務介面。
/// </summary>
public interface IInsuranceBrainService
{
    /// <summary>
    /// 全量商品 JSON 字串。
    /// </summary>
    string AllProductsJson { get; }

    /// <summary>
    /// 確保商品資料已完成載入。
    /// </summary>
    void EnsureInitialized();
}
