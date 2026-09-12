using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantPlatform.Core.MeetingRooms;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Buildings;

namespace TenantPlatform.Web.Services.MeetingRooms;

public class MeetingRoomService(
    IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
    ICurrentUserContextService userContext,
    ITenantAuthorizationService authorization) : IMeetingRoomService
{
    public async Task<List<MeetingRoomDto>> ListAsync(Guid accountId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        var admin = await authorization.CanManageMeetingRoomsAsync(cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await Query(db, accountId, admin, includeInactive).OrderBy(x => x.BuildingName).ThenBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<MeetingRoomDto?> GetAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, false, cancellationToken);
        var admin = await authorization.CanManageMeetingRoomsAsync(cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await Query(db, accountId, admin, true).SingleOrDefaultAsync(x => x.Id == roomId, cancellationToken);
    }

    private static IQueryable<MeetingRoomDto> Query(TenantPlatformDbContext db, Guid accountId, bool administration, bool includeInactive) =>
        from room in db.MeetingRooms.AsNoTracking()
        join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
        join mapping in db.MeetingRoomCalendars.AsNoTracking().Where(x => x.AccountId == accountId)
            on room.Id equals mapping.MeetingRoomId into mappings
        from calendar in mappings.DefaultIfEmpty()
        where room.AccountId == accountId && building.AccountId == accountId &&
            (includeInactive || (room.IsActive && building.IsActive))
        select new MeetingRoomDto
        {
            Id = room.Id, BuildingId = room.BuildingId, BuildingName = building.Name,
            Name = room.Name, Description = room.Description, Capacity = room.Capacity, IsActive = room.IsActive,
            CalendarIntegrationId = administration && calendar != null ? calendar.CalendarIntegrationId : null,
            ExternalCalendarId = administration && calendar != null ? calendar.ExternalCalendarId : null,
            ExternalResourceEmail = administration && calendar != null ? calendar.ExternalResourceEmail : null
        };

    public async Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, true, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Buildings.AsNoTracking().Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Name).Select(x => new BuildingListItemDto { Id = x.Id, Name = x.Name }).ToListAsync(cancellationToken);
    }

    public Task<Guid> CreateAsync(Guid accountId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync(accountId, null, request, cancellationToken);

    public async Task UpdateAsync(Guid accountId, Guid roomId, SaveMeetingRoomRequest request, CancellationToken cancellationToken = default) =>
        await SaveAsync(accountId, roomId, request, cancellationToken);

    private async Task<Guid> SaveAsync(Guid accountId, Guid? roomId, SaveMeetingRoomRequest request, CancellationToken cancellationToken)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 ||
            request.Description?.Length > 4000 || request.Capacity is <= 0)
            throw new MeetingRoomValidationException("MeetingRoomInvalidDetails");

        if (request.CalendarIntegrationId.HasValue &&
            (string.IsNullOrWhiteSpace(request.ExternalCalendarId) || request.ExternalCalendarId.Trim().Length > 512 ||
             request.ExternalResourceEmail?.Length > 320 ||
             (!string.IsNullOrWhiteSpace(request.ExternalResourceEmail) &&
              !new EmailAddressAttribute().IsValid(request.ExternalResourceEmail.Trim()))))
            throw new MeetingRoomValidationException("MeetingRoomInvalidCalendar");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Buildings.AnyAsync(x => x.AccountId == accountId && x.Id == request.BuildingId, cancellationToken))
            throw new MeetingRoomValidationException("BuildingNotFound");
        if (request.CalendarIntegrationId.HasValue && !await db.CalendarIntegrations.AnyAsync(x =>
                x.AccountId == accountId && x.Id == request.CalendarIntegrationId.Value, cancellationToken))
            throw new MeetingRoomValidationException("CalendarIntegrationNotFound");

        var room = roomId.HasValue
            ? await db.MeetingRooms.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == roomId.Value, cancellationToken)
                ?? throw new MeetingRoomValidationException("MeetingRoomNotFound")
            : new MeetingRoom { Id = Guid.NewGuid(), AccountId = accountId };
        if (!roomId.HasValue) db.MeetingRooms.Add(room);
        room.BuildingId = request.BuildingId;
        room.Name = request.Name.Trim();
        room.Description = request.Description?.Trim();
        room.Capacity = request.Capacity;
        room.IsActive = request.IsActive;

        var mapping = await db.MeetingRoomCalendars.SingleOrDefaultAsync(x =>
            x.AccountId == accountId && x.MeetingRoomId == room.Id, cancellationToken);
        if (!request.CalendarIntegrationId.HasValue)
        {
            if (mapping is not null) db.MeetingRoomCalendars.Remove(mapping);
        }
        else
        {
            var externalId = request.ExternalCalendarId!.Trim();
            if (await db.MeetingRoomCalendars.AnyAsync(x => x.AccountId == accountId &&
                    x.CalendarIntegrationId == request.CalendarIntegrationId.Value &&
                    x.ExternalCalendarId == externalId && x.MeetingRoomId != room.Id, cancellationToken))
                throw new MeetingRoomValidationException("MeetingRoomCalendarAlreadyMapped");
            if (mapping is null)
            {
                mapping = new MeetingRoomCalendar { Id = Guid.NewGuid(), AccountId = accountId, MeetingRoomId = room.Id };
                db.MeetingRoomCalendars.Add(mapping);
            }
            mapping.CalendarIntegrationId = request.CalendarIntegrationId.Value;
            mapping.ExternalCalendarId = externalId;
            mapping.ExternalResourceEmail = request.ExternalResourceEmail?.Trim();
        }

        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new MeetingRoomValidationException("MeetingRoomCalendarAlreadyMapped");
        }
        return room.Id;
    }

    public async Task DeactivateAsync(Guid accountId, Guid roomId, CancellationToken cancellationToken = default)
    {
        await MeetingRoomAccess.RequireAsync(accountId, userContext, authorization, true, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var room = await db.MeetingRooms.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == roomId, cancellationToken)
            ?? throw new MeetingRoomValidationException("MeetingRoomNotFound");
        room.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
    }
}
