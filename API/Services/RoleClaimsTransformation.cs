using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace API.Services;

/// <summary>
/// 開發模式下補上預設角色，滿足角色授權需求。
/// </summary>
public sealed class RoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var hasRole = identity.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "CLAF305");
        if (!hasRole)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, "CLAF305"));
        }

        return Task.FromResult(principal);
    }
}
