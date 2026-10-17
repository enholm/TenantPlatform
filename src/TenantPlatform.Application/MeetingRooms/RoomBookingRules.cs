namespace TenantPlatform.Application.MeetingRooms;

internal static class RoomBookingRules
{
    public static void Validate(SaveRoomBookingRequest request)
    {
        ValidateInterval(request.StartUtc, request.EndUtc);
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 200 || request.Description?.Length > 4000)
            throw new MeetingRoomValidationException("RoomBookingInvalidDetails");
    }

    public static void ValidateInterval(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) throw new MeetingRoomValidationException("RoomBookingInvalidInterval");
    }
}
