using API.Attributes;
using API.Contracts;
using API.Infrastructure;

namespace API.Services;

/// <summary>
/// 全量車險商品知識庫服務。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class InsuranceBrainService : IInsuranceBrainService
{
    private readonly IInsuranceProductRepository _insuranceProductRepository;
    private readonly CustomLogger _logger;
    private readonly object _initializeLock = new();
    private volatile bool _isInitialized;

    public InsuranceBrainService(
        IInsuranceProductRepository insuranceProductRepository,
        ILogger<InsuranceBrainService> logger)
    {
        _insuranceProductRepository = insuranceProductRepository;
        _logger = logger.ToCustomLogger();
        AllProductsJson = "[]";
    }

    public string AllProductsJson { get; private set; }

    public void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        lock (_initializeLock)
        {
            if (_isInitialized)
            {
                return;
            }

            AllProductsJson = _insuranceProductRepository.LoadAllProductsJson();
            _isInitialized = true;
            _logger.Info("insurance brain initialized. payloadLength: {}", AllProductsJson.Length);
        }
    }
}
