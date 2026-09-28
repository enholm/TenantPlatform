using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingLifecycleAndReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountableEquipment",
                table: "leasing_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "leasing_equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RegisteredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatusDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReplacementEquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_equipment", x => x.Id);
                    table.UniqueConstraint("AK_leasing_equipment_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_equipment_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_equipment_leasing_acquisitions_AccountId_Acquisitio~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_equipment_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_followups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedExplicitly = table.Column<bool>(type: "boolean", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_followups", x => x.Id);
                    table.UniqueConstraint("AK_leasing_followups_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_followups_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_followups_leasing_acquisitions_AccountId_Acquisitio~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_followups_leasing_frameworks_AccountId_FrameworkId",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_lifecycle_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InputJson = table.Column<string>(type: "jsonb", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_lifecycle_events", x => x.Id);
                    table.UniqueConstraint("AK_leasing_lifecycle_events_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_lifecycle_events_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_lifecycle_events_leasing_acquisitions_AccountId_Acq~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_lifecycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AgreedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NoticeBasis = table.Column<int>(type: "integer", nullable: false),
                    ExplicitNoticeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeCount = table.Column<int>(type: "integer", nullable: true),
                    AutomaticExtension = table.Column<bool>(type: "boolean", nullable: true),
                    ExtensionMonths = table.Column<int>(type: "integer", nullable: true),
                    ReturnDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClosureKind = table.Column<int>(type: "integer", nullable: true),
                    DocumentReviewComplete = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentPlanReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentsChanged = table.Column<bool>(type: "boolean", nullable: false),
                    PlanAtExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_lifecycles", x => x.Id);
                    table.UniqueConstraint("AK_leasing_lifecycles_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_lifecycles_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_lifecycles_leasing_acquisitions_AccountId_Acquisiti~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_notification_settings",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Days = table.Column<int[]>(type: "integer[]", nullable: false),
                    ExtraRecipientIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_notification_settings", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_leasing_notification_settings_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FollowupId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysBefore = table.Column<int>(type: "integer", nullable: false),
                    ScheduledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_notifications", x => x.Id);
                    table.UniqueConstraint("AK_leasing_notifications_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_notifications_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_notifications_leasing_followups_AccountId_FollowupId",
                        columns: x => new { x.AccountId, x.FollowupId },
                        principalTable: "leasing_followups",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_dispositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reversed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_dispositions", x => x.Id);
                    table.UniqueConstraint("AK_leasing_dispositions_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_dispositions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dispositions_leasing_acquisitions_AccountId_Acquisi~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dispositions_leasing_equipment_AccountId_EquipmentId",
                        columns: x => new { x.AccountId, x.EquipmentId },
                        principalTable: "leasing_equipment",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dispositions_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_dispositions_leasing_lifecycle_events_AccountId_Eve~",
                        columns: x => new { x.AccountId, x.EventId },
                        principalTable: "leasing_lifecycle_events",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dispositions_AccountId_AcquisitionId",
                table: "leasing_dispositions",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dispositions_AccountId_EquipmentId",
                table: "leasing_dispositions",
                columns: new[] { "AccountId", "EquipmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dispositions_AccountId_EventId",
                table: "leasing_dispositions",
                columns: new[] { "AccountId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_dispositions_AccountId_ItemId_Reversed",
                table: "leasing_dispositions",
                columns: new[] { "AccountId", "ItemId", "Reversed" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_equipment_AccountId_AcquisitionId_Status",
                table: "leasing_equipment",
                columns: new[] { "AccountId", "AcquisitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_equipment_AccountId_InternalId",
                table: "leasing_equipment",
                columns: new[] { "AccountId", "InternalId" },
                unique: true,
                filter: "\"InternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_equipment_AccountId_ItemId",
                table: "leasing_equipment",
                columns: new[] { "AccountId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_equipment_AccountId_SerialNumber",
                table: "leasing_equipment",
                columns: new[] { "AccountId", "SerialNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_followups_AccountId_AcquisitionId",
                table: "leasing_followups",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_followups_AccountId_FrameworkId",
                table: "leasing_followups",
                columns: new[] { "AccountId", "FrameworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_followups_AccountId_Key",
                table: "leasing_followups",
                columns: new[] { "AccountId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_followups_AccountId_Status_DueDate",
                table: "leasing_followups",
                columns: new[] { "AccountId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_lifecycle_events_AccountId_AcquisitionId_RecordedUtc",
                table: "leasing_lifecycle_events",
                columns: new[] { "AccountId", "AcquisitionId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_lifecycles_AccountId_AcquisitionId",
                table: "leasing_lifecycles",
                columns: new[] { "AccountId", "AcquisitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_notifications_AccountId_FollowupId_RecipientUserId_~",
                table: "leasing_notifications",
                columns: new[] { "AccountId", "FollowupId", "RecipientUserId", "DaysBefore" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_notifications_AccountId_RecipientUserId_Status",
                table: "leasing_notifications",
                columns: new[] { "AccountId", "RecipientUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leasing_dispositions");

            migrationBuilder.DropTable(
                name: "leasing_lifecycles");

            migrationBuilder.DropTable(
                name: "leasing_notification_settings");

            migrationBuilder.DropTable(
                name: "leasing_notifications");

            migrationBuilder.DropTable(
                name: "leasing_equipment");

            migrationBuilder.DropTable(
                name: "leasing_lifecycle_events");

            migrationBuilder.DropTable(
                name: "leasing_followups");

            migrationBuilder.DropColumn(
                name: "CountableEquipment",
                table: "leasing_items");
        }
    }
}
