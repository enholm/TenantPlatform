using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Authentication;

public class LoginResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public User? User { get; init; }

    public static LoginResult Success(User user) =>
        new()
        {
            Succeeded = true,
            User = user
        };

    public static LoginResult Failure(string errorMessage) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage
        };
}
