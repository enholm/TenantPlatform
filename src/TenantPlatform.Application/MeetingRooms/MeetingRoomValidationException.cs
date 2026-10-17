namespace TenantPlatform.Application.MeetingRooms;

public class MeetingRoomValidationException(string resourceKey) : Exception(resourceKey);
