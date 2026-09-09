using System.Security.Claims;
using API.Contracts;
using API.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.G1;

/// <summary>
/// 驗證控制器。
/// </summary>
[Route("api/g1/auth")]
[ApiController]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly ISystemUserLoginService _systemUserLoginService;

    public AuthController(ISystemUserLoginService systemUserLoginService)
    {
        _systemUserLoginService = systemUserLoginService;
    }

    /// <summary>
    /// 取得目前登入狀態。
    /// </summary>
    [HttpGet("status")]
    public ResponseDataSchema<SystemUserLoginResult?> Status()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return new ResponseDataSchema<SystemUserLoginResult?>
            {
                Code = 0,
                Msg = "尚未登入",
                Data = null,
                TraceId = HttpContext.TraceIdentifier
            };
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ResponseDataSchema<SystemUserLoginResult?>
            {
                Code = 0,
                Msg = "尚未登入",
                Data = null,
                TraceId = HttpContext.TraceIdentifier
            };
        }

        return new ResponseDataSchema<SystemUserLoginResult?>
        {
            Code = 0,
            Msg = "已登入",
            Data = new SystemUserLoginResult
            {
                UserId = userId,
                UserName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty
            },
            TraceId = HttpContext.TraceIdentifier
        };
    }

    /// <summary>
    /// 使用者登入。
    /// </summary>
    [HttpPost("login")]
    public async Task<ResponseDataSchema<SystemUserLoginResult>> Login(
        [FromBody] UserLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _systemUserLoginService.LoginAsync(request.UserId, cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("登入失敗");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId),
            new(ClaimTypes.Name, result.UserName),
            new(ClaimTypes.Role, "CLAF305")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true
            });

        return new ResponseDataSchema<SystemUserLoginResult>
        {
            Code = 0,
            Msg = "登入成功",
            Data = result,
            TraceId = HttpContext.TraceIdentifier
        };
    }

    /// <summary>
    /// 使用者登出。
    /// </summary>
    [HttpPost("logout")]
    public async Task<ResponseDataSchema<object?>> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return new ResponseDataSchema<object?>
        {
            Code = 0,
            Msg = "登出成功",
            Data = null,
            TraceId = HttpContext.TraceIdentifier
        };
    }
}
