using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingOrdersAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_leasing_document_parent",
                table: "leasing_documents");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "leasing_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leasing_limit_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousLimit = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    NewLimit = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DocumentReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProposedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_limit_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_limit_changes_leasing_frameworks_AccountId_Framewor~",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProposedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_orders", x => x.Id);
                    table.UniqueConstraint("AK_leasing_orders_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_orders_leasing_frameworks_AccountId_FrameworkId",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_order_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_order_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_order_events_leasing_orders_AccountId_OrderId",
                        columns: x => new { x.AccountId, x.OrderId },
                        principalTable: "leasing_orders",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    ClassificationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Fulfilled = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    Unreserved = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_order_lines", x => x.Id);
                    table.UniqueConstraint("AK_leasing_order_lines_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_order_scope", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Fulfilled\" >= 0 AND \"Unreserved\" >= 0 AND \"Fulfilled\" + \"Unreserved\" <= CASE WHEN \"Method\" = 1 THEN \"Quantity\" ELSE 1 END");
                    table.ForeignKey(
                        name: "FK_leasing_order_lines_leasing_orders_AccountId_OrderId",
                        columns: x => new { x.AccountId, x.OrderId },
                        principalTable: "leasing_orders",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_order_realizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    ApprovedNet = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    ApprovedVat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    ActualNet = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    ActualVat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Reversed = table.Column<bool>(type: "boolean", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_order_realizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_order_realizations_leasing_acquisitions_AccountId_A~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_order_realizations_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_order_realizations_leasing_order_lines_AccountId_Or~",
                        columns: x => new { x.AccountId, x.OrderLineId },
                        principalTable: "leasing_order_lines",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_order_realizations_leasing_orders_AccountId_OrderId",
                        columns: x => new { x.AccountId, x.OrderId },
                        principalTable: "leasing_orders",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_documents_AccountId_OrderId",
                table: "leasing_documents",
                columns: new[] { "AccountId", "OrderId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_leasing_document_parent",
                table: "leasing_documents",
                sql: "num_nonnulls(\"FrameworkId\", \"AcquisitionId\", \"OrderId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_limit_changes_AccountId_FrameworkId",
                table: "leasing_limit_changes",
                columns: new[] { "AccountId", "FrameworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_events_AccountId_OrderId",
                table: "leasing_order_events",
                columns: new[] { "AccountId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_lines_AccountId_OrderId",
                table: "leasing_order_lines",
                columns: new[] { "AccountId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_realizations_AccountId_AcquisitionId",
                table: "leasing_order_realizations",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_realizations_AccountId_ItemId",
                table: "leasing_order_realizations",
                columns: new[] { "AccountId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_realizations_AccountId_OrderId",
                table: "leasing_order_realizations",
                columns: new[] { "AccountId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_order_realizations_AccountId_OrderLineId",
                table: "leasing_order_realizations",
                columns: new[] { "AccountId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_orders_AccountId_FrameworkId",
                table: "leasing_orders",
                columns: new[] { "AccountId", "FrameworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_orders_AccountId_Number",
                table: "leasing_orders",
                columns: new[] { "AccountId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_leasing_documents_leasing_orders_AccountId_OrderId",
                table: "leasing_documents",
                columns: new[] { "AccountId", "OrderId" },
                principalTable: "leasing_orders",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leasing_documents_leasing_orders_AccountId_OrderId",
                table: "leasing_documents");

            migrationBuilder.DropTable(
                name: "leasing_limit_changes");

            migrationBuilder.DropTable(
                name: "leasing_order_events");

            migrationBuilder.DropTable(
                name: "leasing_order_realizations");

            migrationBuilder.DropTable(
                name: "leasing_order_lines");

            migrationBuilder.DropTable(
                name: "leasing_orders");

            migrationBuilder.DropIndex(
                name: "IX_leasing_documents_AccountId_OrderId",
                table: "leasing_documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_leasing_document_parent",
                table: "leasing_documents");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "leasing_documents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_leasing_document_parent",
                table: "leasing_documents",
                sql: "(\"FrameworkId\" IS NULL) <> (\"AcquisitionId\" IS NULL)");
        }
    }
}
