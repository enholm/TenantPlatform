using TenantPlatform.Application.Accounts;

namespace TenantPlatform.Web.Services.Accounts;

public sealed class AccountService(TenantPlatform.Application.Accounts.AccountService service) : IAccountService
{
    public Task<List<AccountListItemDto>> GetAccountsAsync(CancellationToken cancellationToken = default) => service.GetAccountsAsync(cancellationToken);
    public Task<AccountDetailsDto?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetAccountAsync(accountId, cancellationToken);
    public Task<Guid> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default) => service.CreateAccountAsync(request, cancellationToken);
    public Task UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default) => service.UpdateAccountAsync(accountId, request, cancellationToken);
    public Task<AccountDeleteCheckResult> CanDeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => service.CanDeleteAccountAsync(accountId, cancellationToken);
    public Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => service.DeleteAccountAsync(accountId, cancellationToken);
}
