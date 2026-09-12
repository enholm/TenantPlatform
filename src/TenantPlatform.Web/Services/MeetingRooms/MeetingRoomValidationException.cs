namespace TenantPlatform.Web.Services.MeetingRooms;

public class MeetingRoomValidationException(string resourceKey) : Exception(resourceKey);
