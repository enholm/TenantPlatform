using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingInvoiceImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceNetAdjustment",
                table: "leasing_items",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceVatAdjustment",
                table: "leasing_items",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CreditNotesReleaseLimit",
                table: "leasing_frameworks",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditNetTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditVatTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReleasedNetTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReleasedVatTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversedNetTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversedVatTotal",
                table: "leasing_acquisitions",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "leasing_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Processing = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProcessingStartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessingToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingError = table.Column<string>(type: "text", nullable: true),
                    ReviewJson = table.Column<string>(type: "jsonb", nullable: false),
                    ApprovedJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SupplierIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    ReleasesLimit = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_invoices", x => x.Id);
                    table.UniqueConstraint("AK_leasing_invoices_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_invoices_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_invoices_leasing_acquisitions_AccountId_Acquisition~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_invoice_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_invoice_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_invoice_history_leasing_invoices_AccountId_InvoiceId",
                        columns: x => new { x.AccountId, x.InvoiceId },
                        principalTable: "leasing_invoices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_invoice_interpretations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_invoice_interpretations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leasing_invoice_interpretations_leasing_invoices_AccountId_~",
                        columns: x => new { x.AccountId, x.InvoiceId },
                        principalTable: "leasing_invoices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditedLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceLineId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    CreatedItem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_invoice_lines", x => x.Id);
                    table.UniqueConstraint("AK_leasing_invoice_lines_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_leasing_invoice_lines_leasing_invoice_lines_AccountId_Credi~",
                        columns: x => new { x.AccountId, x.CreditedLineId },
                        principalTable: "leasing_invoice_lines",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_invoice_lines_leasing_invoices_AccountId_InvoiceId",
                        columns: x => new { x.AccountId, x.InvoiceId },
                        principalTable: "leasing_invoices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_invoice_lines_leasing_items_AccountId_ItemId",
                        columns: x => new { x.AccountId, x.ItemId },
                        principalTable: "leasing_items",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoice_history_AccountId_InvoiceId",
                table: "leasing_invoice_history",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoice_interpretations_AccountId_InvoiceId",
                table: "leasing_invoice_interpretations",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoice_lines_AccountId_CreditedLineId",
                table: "leasing_invoice_lines",
                columns: new[] { "AccountId", "CreditedLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoice_lines_AccountId_InvoiceId",
                table: "leasing_invoice_lines",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoice_lines_AccountId_ItemId",
                table: "leasing_invoice_lines",
                columns: new[] { "AccountId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_AccountId_AcquisitionId",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_AccountId_FileHash",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_AccountId_SupplierIdentity_Kind_Number",
                table: "leasing_invoices",
                columns: new[] { "AccountId", "SupplierIdentity", "Kind", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_invoices_Processing_ProcessingStartedUtc",
                table: "leasing_invoices",
                columns: new[] { "Processing", "ProcessingStartedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leasing_invoice_history");

            migrationBuilder.DropTable(
                name: "leasing_invoice_interpretations");

            migrationBuilder.DropTable(
                name: "leasing_invoice_lines");

            migrationBuilder.DropTable(
                name: "leasing_invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceNetAdjustment",
                table: "leasing_items");

            migrationBuilder.DropColumn(
                name: "InvoiceVatAdjustment",
                table: "leasing_items");

            migrationBuilder.DropColumn(
                name: "CreditNotesReleaseLimit",
                table: "leasing_frameworks");

            migrationBuilder.DropColumn(
                name: "CreditNetTotal",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "CreditVatTotal",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "ReleasedNetTotal",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "ReleasedVatTotal",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "ReversedNetTotal",
                table: "leasing_acquisitions");

            migrationBuilder.DropColumn(
                name: "ReversedVatTotal",
                table: "leasing_acquisitions");
        }
    }
}
