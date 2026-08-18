using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenantPlatform.Core.Auditing;

namespace TenantPlatform.Infrastructure.Auditing;

public class AuditSaveChangesInterceptor
    : SaveChangesInterceptor
{
    private readonly IAuditUserContext _auditUserContext;

    public AuditSaveChangesInterceptor(
        IAuditUserContext auditUserContext)
    {
        _auditUserContext = auditUserContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);

        return base.SavingChanges(
            eventData,
            result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private void AddAuditEntries(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var entries = dbContext.ChangeTracker
            .Entries()
            .Where(x =>
                x.Entity is not AuditLog &&
                x.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            var auditLog = CreateAuditLog(
                entry,
                timestamp);

            dbContext.Set<AuditLog>().Add(auditLog);
        }
    }

    private AuditLog CreateAuditLog(
        EntityEntry entry,
        DateTimeOffset timestamp)
    {
        var changes = new Dictionary<string, object?>();

        if (entry.State == EntityState.Added)
        {
            foreach (var property in entry.Properties)
            {
                changes[property.Metadata.Name] =
                    new
                    {
                        Old = (object?)null,
                        New = property.CurrentValue
                    };
            }
        }
        else if (entry.State == EntityState.Deleted)
        {
            foreach (var property in entry.Properties)
            {
                changes[property.Metadata.Name] =
                    new
                    {
                        Old = property.OriginalValue,
                        New = (object?)null
                    };
            }
        }
        else
        {
            foreach (var property in entry.Properties)
            {
                if (!property.IsModified)
                {
                    continue;
                }

                changes[property.Metadata.Name] =
                    new
                    {
                        Old = property.OriginalValue,
                        New = property.CurrentValue
                    };
            }
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),

            AccountId =
                GetAccountId(entry)
                ?? _auditUserContext.AccountId,

            UserId = _auditUserContext.UserId,

            UserEmail = _auditUserContext.Email,

            TimestampUtc = timestamp,

            EntityType =
                entry.Metadata.ClrType.Name,

            EntityId =
                GetEntityId(entry),

            Action =
                entry.State.ToString(),

            ChangesJson =
                JsonSerializer.Serialize(changes)
        };
    }

    private static Guid? GetAccountId(
        EntityEntry entry)
    {
        var property =
            entry.Properties
                .FirstOrDefault(
                    x => x.Metadata.Name == "AccountId");
        if (property?.CurrentValue is Guid accountId)
        {
            return accountId;
        }
        return null;
    }

    private static string GetEntityId(
        EntityEntry entry)
    {
        var key =
            entry.Metadata.FindPrimaryKey();

        if (key is null)
        {
            return string.Empty;
        }

        var values = key.Properties
            .Select(property =>
            {
                var value =
                    entry.Property(property.Name).CurrentValue;

                return value?.ToString() ?? string.Empty;
            });

        return string.Join(",", values);
    }
}