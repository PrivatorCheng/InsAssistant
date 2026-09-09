namespace API.Models;

/// <summary>
/// 使用者登入請求。
/// </summary>
public sealed class UserLoginRequest
{
    public required string UserId { get; set; }
}
