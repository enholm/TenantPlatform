using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Authentication;

public class AuthenticationResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public User? User { get; init; }

    public static AuthenticationResult Success(User user) =>
        new()
        {
            Succeeded = true,
            User = user
        };

    public static AuthenticationResult Failure(string errorMessage) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage
        };
}
