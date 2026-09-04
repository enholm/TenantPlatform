namespace TenantPlatform.Web.Services.Accounts;

public interface IAccountService
{
    Task<List<AccountListItemDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountDetailsDto?> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<AccountDeleteCheckResult> CanDeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}

