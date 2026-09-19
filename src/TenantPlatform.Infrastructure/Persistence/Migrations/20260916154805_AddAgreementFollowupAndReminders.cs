using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementFollowupAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeadlinesInitialized",
                table: "agreements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "agreements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int[]>(
                name: "ReminderDays",
                table: "agreements",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int>(
                name: "ReminderMode",
                table: "agreements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RenewalDate",
                table: "agreements",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agreement_deadlines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_deadlines", x => x.Id);
                    table.UniqueConstraint("AK_agreement_deadlines_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_deadlines_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadlines_users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadlines_users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadlines_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadlines_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_reminder_settings",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Days = table.Column<int[]>(type: "integer[]", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SendAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_reminder_settings", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_agreement_reminder_settings_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_deadline_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeadlineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_deadline_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_deadline_history_agreement_deadlines_AccountId_De~",
                        columns: x => new { x.AccountId, x.DeadlineId },
                        principalTable: "agreement_deadlines",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadline_history_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_deadline_history_users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeadlineId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysBefore = table.Column<int>(type: "integer", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ScheduledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransportId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResultKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_reminders", x => x.Id);
                    table.CheckConstraint("CK_agreement_reminder_days", "\"DaysBefore\" >= 0");
                    table.ForeignKey(
                        name: "FK_agreement_reminders_agreement_deadlines_AccountId_DeadlineId",
                        columns: x => new { x.AccountId, x.DeadlineId },
                        principalTable: "agreement_deadlines",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_reminders_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadline_history_AccountId_DeadlineId_CreatedUtc",
                table: "agreement_deadline_history",
                columns: new[] { "AccountId", "DeadlineId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadline_history_ActorUserId",
                table: "agreement_deadline_history",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadline_history_AssignedUserId",
                table: "agreement_deadline_history",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_AccountId_AgreementId_State",
                table: "agreement_deadlines",
                columns: new[] { "AccountId", "AgreementId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_AccountId_State_Status_DueDate",
                table: "agreement_deadlines",
                columns: new[] { "AccountId", "State", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_AssignedUserId",
                table: "agreement_deadlines",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_CompletedByUserId",
                table: "agreement_deadlines",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_CreatedByUserId",
                table: "agreement_deadlines",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_deadlines_UpdatedByUserId",
                table: "agreement_deadlines",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_reminders_AccountId_DeadlineId_DaysBefore",
                table: "agreement_reminders",
                columns: new[] { "AccountId", "DeadlineId", "DaysBefore" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_reminders_AccountId_Status_NextAttemptUtc",
                table: "agreement_reminders",
                columns: new[] { "AccountId", "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_reminders_RecipientUserId",
                table: "agreement_reminders",
                column: "RecipientUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_deadline_history");

            migrationBuilder.DropTable(
                name: "agreement_reminder_settings");

            migrationBuilder.DropTable(
                name: "agreement_reminders");

            migrationBuilder.DropTable(
                name: "agreement_deadlines");

            migrationBuilder.DropColumn(
                name: "DeadlinesInitialized",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "ReminderDays",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "ReminderMode",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "RenewalDate",
                table: "agreements");
        }
    }
}
