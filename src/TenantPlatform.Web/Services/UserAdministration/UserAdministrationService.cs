using TenantPlatform.Application.UserAdministration;
namespace TenantPlatform.Web.Services.UserAdministration;

public sealed class UserAdministrationService(TenantPlatform.Application.UserAdministration.UserAdministrationService service) : IUserAdministrationService
{
    public Task<List<UserListItemDto>> GetUsersAsync(CancellationToken cancellationToken = default) => service.GetUsersAsync(cancellationToken);
    public Task<UserDetailsDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) => service.GetUserAsync(userId, cancellationToken);
    public Task<Guid> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) => service.CreateUserAsync(request, cancellationToken);
    public Task UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default) => service.UpdateUserAsync(userId, request, cancellationToken);
    public Task<IReadOnlyList<UserAccountAccessDto>> GetUserAccessAsync(Guid userId, CancellationToken cancellationToken = default) => service.GetUserAccessAsync(userId, cancellationToken);
    public Task AddUserRoleAsync(Guid userId, AddUserRoleRequest request, CancellationToken cancellationToken = default) => service.AddUserRoleAsync(userId, request, cancellationToken);
    public Task RemoveUserRoleAsync(Guid userId, Guid userAccountRoleId, CancellationToken cancellationToken = default) => service.RemoveUserRoleAsync(userId, userAccountRoleId, cancellationToken);
    public Task<IReadOnlyList<UserAccountOptionDto>> GetAvailableAccountsAsync(CancellationToken cancellationToken = default) => service.GetAvailableAccountsAsync(cancellationToken);
    public Task<IReadOnlyList<UserOrganizationOptionDto>> GetAvailableOrganizationsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetAvailableOrganizationsAsync(accountId, cancellationToken);
    public Task<IReadOnlyList<UserBuildingOptionDto>> GetAvailableBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetAvailableBuildingsAsync(accountId, cancellationToken);
}
