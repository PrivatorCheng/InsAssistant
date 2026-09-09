using API.Models;

namespace API.Contracts;

public interface ISystemUserLoginService
{
    Task<SystemUserLoginResult?> LoginAsync(string userId, CancellationToken cancellationToken = default);
}
