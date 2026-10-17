using System.ComponentModel.DataAnnotations;
namespace TenantPlatform.Application.MeetingRooms;

internal static class MeetingRoomRules
{
    public static void Validate(SaveMeetingRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 ||
            request.Description?.Length > 4000 || request.Capacity is <= 0)
            throw new MeetingRoomValidationException("MeetingRoomInvalidDetails");

        if (request.CalendarIntegrationId.HasValue &&
            (string.IsNullOrWhiteSpace(request.ExternalCalendarId) || request.ExternalCalendarId.Trim().Length > 512 ||
             request.ExternalResourceEmail?.Length > 320 ||
             (!string.IsNullOrWhiteSpace(request.ExternalResourceEmail) &&
              !new EmailAddressAttribute().IsValid(request.ExternalResourceEmail.Trim()))))
            throw new MeetingRoomValidationException("MeetingRoomInvalidCalendar");

    }
}
