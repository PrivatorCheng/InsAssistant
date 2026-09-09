namespace API.Contracts;

/// <summary>
/// Gemini 脈絡快取服務介面。
/// </summary>
public interface IGeminiContextCacheService
{
    /// <summary>
    /// 目前可用的 CachedContent 名稱。
    /// </summary>
    string? CachedContentName { get; }

    /// <summary>
    /// 確保 Gemini 脈絡快取已建立完成。
    /// </summary>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
