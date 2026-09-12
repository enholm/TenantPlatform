using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingRoomsAndBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_integrations", x => x.Id);
                    table.UniqueConstraint("AK_calendar_integrations_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_calendar_integrations_provider", "\"Provider\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_calendar_integrations_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_rooms", x => x.Id);
                    table.UniqueConstraint("AK_meeting_rooms_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_meeting_rooms_capacity", "\"Capacity\" IS NULL OR \"Capacity\" > 0");
                    table.ForeignKey(
                        name: "FK_meeting_rooms_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_meeting_rooms_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_room_calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarIntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCalendarId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExternalResourceEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_room_calendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meeting_room_calendars_calendar_integrations_AccountId_Cale~",
                        columns: x => new { x.AccountId, x.CalendarIntegrationId },
                        principalTable: "calendar_integrations",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_meeting_room_calendars_meeting_rooms_AccountId_MeetingRoomId",
                        columns: x => new { x.AccountId, x.MeetingRoomId },
                        principalTable: "meeting_rooms",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_bookings", x => x.Id);
                    table.CheckConstraint("CK_room_bookings_interval", "\"EndUtc\" > \"StartUtc\"");
                    table.CheckConstraint("CK_room_bookings_status", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_room_bookings_meeting_rooms_AccountId_MeetingRoomId",
                        columns: x => new { x.AccountId, x.MeetingRoomId },
                        principalTable: "meeting_rooms",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_bookings_users_OrganizerUserId",
                        column: x => x.OrganizerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_integrations_AccountId",
                table: "calendar_integrations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_room_calendars_AccountId_CalendarIntegrationId_Exte~",
                table: "meeting_room_calendars",
                columns: new[] { "AccountId", "CalendarIntegrationId", "ExternalCalendarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_room_calendars_AccountId_MeetingRoomId",
                table: "meeting_room_calendars",
                columns: new[] { "AccountId", "MeetingRoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_rooms_AccountId_BuildingId",
                table: "meeting_rooms",
                columns: new[] { "AccountId", "BuildingId" });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_rooms_BuildingId",
                table: "meeting_rooms",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_room_bookings_AccountId_MeetingRoomId_StartUtc_EndUtc",
                table: "room_bookings",
                columns: new[] { "AccountId", "MeetingRoomId", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_room_bookings_OrganizerUserId",
                table: "room_bookings",
                column: "OrganizerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meeting_room_calendars");

            migrationBuilder.DropTable(
                name: "room_bookings");

            migrationBuilder.DropTable(
                name: "calendar_integrations");

            migrationBuilder.DropTable(
                name: "meeting_rooms");
        }
    }
}
