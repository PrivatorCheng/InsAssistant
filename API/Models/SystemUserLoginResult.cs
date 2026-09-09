namespace API.Models;

/// <summary>
/// 使用者登入結果。
/// </summary>
public sealed class SystemUserLoginResult
{
    public required string UserId { get; set; }

    public required string UserName { get; set; }
}
