namespace TenantPlatform.Web.Services.UserAdministration;

public interface IUserAdministrationService
{
    Task<List<UserListItemDto>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<UserDetailsDto?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateUserAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountAccessDto>> GetUserAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddUserRoleAsync(
        Guid userId,
        AddUserRoleRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveUserRoleAsync(
        Guid userId,
        Guid userAccountRoleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountOptionDto>>
        GetAvailableAccountsAsync(
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserOrganizationOptionDto>>
        GetAvailableOrganizationsAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserBuildingOptionDto>>
        GetAvailableBuildingsAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);
}