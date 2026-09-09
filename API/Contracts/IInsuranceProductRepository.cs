namespace API.Contracts;

/// <summary>
/// 保險商品規格資料存取介面。
/// </summary>
public interface IInsuranceProductRepository
{
    /// <summary>
    /// 載入並整併所有車險商品 JSON 內容。
    /// </summary>
    string LoadAllProductsJson();
}
