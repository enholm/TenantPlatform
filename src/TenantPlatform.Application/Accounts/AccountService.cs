using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Localization;

namespace TenantPlatform.Application.Accounts;

public interface IAccountData
{
    Task<List<AccountListItemDto>> ReadListAsync(CancellationToken cancellationToken);
    Task<AccountDetailsDto?> ReadAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IAccountTransaction> BeginAsync(Guid? id, CancellationToken ct);
}
public interface IAccountTransaction : IAsyncDisposable
{
    Account Account { get; }
    Task<bool> HasOwnedDataAsync(CancellationToken ct);
    Task CommitAsync(bool delete, CancellationToken ct);
}
public sealed class AccountService(IAuthorizationService authorization, IAccountData data) : IAccountService
{
    private async Task Require(string permission, Guid? id, CancellationToken ct)
    {
        if (!await authorization.CanAsync(permission, new(id.HasValue ? ResourceKind.PlatformAccount : ResourceKind.Platform, id), ct))
            throw new UnauthorizedAccessException();
    }
    public async Task<List<AccountListItemDto>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await Require(Permissions.Platform.OpenAccounts, null, cancellationToken);
        return await data.ReadListAsync(cancellationToken);
    }
    public async Task<AccountDetailsDto?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await Require(Permissions.Platform.ReadAccount, accountId, cancellationToken);
        return await data.ReadAsync(accountId, cancellationToken);
    }
    public async Task<Guid> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name; var language = request.DefaultLanguage; var active = request.IsActive;
        await Require(Permissions.Platform.CreateAccount, null, cancellationToken);
        ValidateRequest(name, language);
        await using var transaction = await data.BeginAsync(null, cancellationToken);
        await Require(Permissions.Platform.CreateAccount, null, cancellationToken);
        transaction.Account.Name = name.Trim(); transaction.Account.DefaultLanguage = language.Trim(); transaction.Account.IsActive = active;
        await transaction.CommitAsync(false, cancellationToken);
        return transaction.Account.Id;
    }
    public async Task UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name; var language = request.DefaultLanguage; var active = request.IsActive;
        await Require(Permissions.Platform.UpdateAccount, accountId, cancellationToken);
        ValidateRequest(name, language);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        await Require(Permissions.Platform.UpdateAccount, accountId, cancellationToken);
        transaction.Account.Name = name.Trim(); transaction.Account.DefaultLanguage = language.Trim(); transaction.Account.IsActive = active;
        await transaction.CommitAsync(false, cancellationToken);
    }
    public async Task<AccountDeleteCheckResult> CanDeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await Require(Permissions.Platform.DeleteAccount, accountId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        return await transaction.HasOwnedDataAsync(cancellationToken) ? AccountDeleteCheckResult.NotAllowed("AccountHasData") : AccountDeleteCheckResult.Allowed();
    }
    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await Require(Permissions.Platform.DeleteAccount, accountId, cancellationToken);
        await using var transaction = await data.BeginAsync(accountId, cancellationToken);
        await Require(Permissions.Platform.DeleteAccount, accountId, cancellationToken);
        if (await transaction.HasOwnedDataAsync(cancellationToken)) throw new AccountDeleteNotAllowedException("AccountHasData");
        await transaction.CommitAsync(true, cancellationToken);
    }
    private static void ValidateRequest(
        string name,
        string defaultLanguage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "AccountNameRequired");
        }

        if (name.Trim().Length > 200)
        {
            throw new InvalidOperationException(
                "AccountNameTooLong");
        }

        if (string.IsNullOrWhiteSpace(defaultLanguage))
        {
            throw new InvalidOperationException(
                "AccountDefaultLanguageRequired");
        }

        var languageSupported =
            SupportedLanguages.All.Any(
                x => string.Equals(
                    x.Code,
                    defaultLanguage.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (!languageSupported)
        {
            throw new InvalidOperationException(
                "AccountDefaultLanguageNotSupported");
        }
    }
}
