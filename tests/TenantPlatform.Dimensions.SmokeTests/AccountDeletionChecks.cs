using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Accounts;

static class AccountDeletionChecks
{
    public static async Task Run()
    {
        // Isolate each predicate. InMemory intentionally accepts these incomplete graphs;
        // this tests the production deletion decision, never relational FK enforcement.
        foreach (var type in new[] { typeof(Dimension), typeof(DimensionValue), typeof(DimensionTranslation), typeof(DimensionHistory),
            typeof(AgreementDimensionRule), typeof(AgreementDimensionAssignment), typeof(AgreementDimensionHistory),
            typeof(LeasingDimensionRule), typeof(LeasingAgreementDimensionRule), typeof(LeasingFrameworkDimensionAssignment),
            typeof(LeasingAcquisitionDimensionAssignment), typeof(LeasingDimensionSelection), typeof(LeasingAllocationDimension), typeof(LeasingAllocationRow) })
        {
            var factory = new DeleteFactory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var account = Guid.NewGuid(); var other = Guid.NewGuid();
            var row = Activator.CreateInstance(type)!;
            type.GetProperty("AccountId")!.SetValue(row, account);
            foreach (var property in type.GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(Guid) && p.Name != "AccountId")) property.SetValue(row, Guid.NewGuid());
            await using (var db = factory.CreateDbContext())
            { db.Accounts.AddRange(new Account { Id = account }, new Account { Id = other }); db.Add(row); await db.SaveChangesAsync(); }
            var service = new AccountService(factory);
            var blocked = await service.CanDeleteAccountAsync(account);
            Verify.That(!blocked.CanDelete && blocked.Reason == "AccountHasData", type.Name + " alone blocks account deletion without memberships");
            await Verify.Throws<AccountDeleteNotAllowedException>(() => service.DeleteAccountAsync(account), type.Name + " blocks production delete action");
            Verify.That((await service.CanDeleteAccountAsync(other)).CanDelete, type.Name + " belonging to another account does not block empty account");
        }
        var emptyFactory = new DeleteFactory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id = Guid.NewGuid();
        await using (var db = emptyFactory.CreateDbContext()) { db.Accounts.Add(new Account { Id = id }); await db.SaveChangesAsync(); }
        var empty = new AccountService(emptyFactory);
        Verify.That((await empty.CanDeleteAccountAsync(Guid.NewGuid())).Reason == "AccountNotFound", "missing account retains not-found deletion decision");
        await empty.DeleteAccountAsync(id);
        Verify.That((await empty.CanDeleteAccountAsync(id)).Reason == "AccountNotFound", "empty account may still be deleted under unchanged policy");
    }
    sealed class DeleteFactory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
    { public TenantPlatformDbContext CreateDbContext() => new(options); }
}
