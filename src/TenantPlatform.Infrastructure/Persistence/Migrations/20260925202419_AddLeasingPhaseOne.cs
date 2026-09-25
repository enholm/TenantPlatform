using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingPhaseOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leasing_frameworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinanceOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    AcquisitionTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Limit = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IncludesVat = table.Column<bool>(type: "boolean", nullable: false),
                    Terms_Months = table.Column<int>(type: "integer", nullable: false),
                    Terms_InterestKind = table.Column<int>(type: "integer", nullable: false),
                    Terms_AnnualRatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Terms_ReferenceRateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Terms_MarginPercentagePoints = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Terms_PaymentFrequency = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_frameworks", x => x.Id);
                    table.UniqueConstraint("AK_leasing_frameworks_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_leasing_framework_limit", "\"Limit\" >= 0");
                    table.CheckConstraint("CK_leasing_framework_period", "\"AcquisitionTo\" >= \"AcquisitionFrom\"");
                    table.CheckConstraint("CK_leasing_framework_status", "\"Status\" IN (1,2,3)");
                    table.ForeignKey(
                        name: "FK_leasing_frameworks_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_frameworks_organizations_FinanceOrganizationId",
                        column: x => x.FinanceOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_frameworks_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_acquisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SupplierOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NetTotal = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    FinancedAmount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Terms_Months = table.Column<int>(type: "integer", nullable: false),
                    Terms_InterestKind = table.Column<int>(type: "integer", nullable: false),
                    Terms_AnnualRatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Terms_ReferenceRateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Terms_MarginPercentagePoints = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Terms_PaymentFrequency = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_acquisitions", x => x.Id);
                    table.UniqueConstraint("AK_leasing_acquisitions_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_leasing_acquisition_amounts", "\"NetTotal\" >= 0 AND \"VatTotal\" >= 0 AND \"GrossTotal\" = \"NetTotal\" + \"VatTotal\" AND \"FinancedAmount\" >= 0");
                    table.CheckConstraint("CK_leasing_acquisition_dates", "\"EndDate\" > \"PurchaseDate\"");
                    table.CheckConstraint("CK_leasing_acquisition_status", "\"Status\" IN (1,2,3)");
                    table.ForeignKey(
                        name: "FK_leasing_acquisitions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_acquisitions_leasing_frameworks_AccountId_Framework~",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_acquisitions_organizations_FinanceOrganizationId",
                        column: x => x.FinanceOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_acquisitions_organizations_SupplierOrganizationId",
                        column: x => x.SupplierOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_acquisitions_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_documents", x => x.Id);
                    table.CheckConstraint("CK_leasing_document_parent", "(\"FrameworkId\" IS NULL) <> (\"AcquisitionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_leasing_documents_leasing_acquisitions_AccountId_Acquisitio~",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_documents_leasing_frameworks_AccountId_FrameworkId",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_documents_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_history", x => x.Id);
                    table.CheckConstraint("CK_leasing_history_parent", "(\"FrameworkId\" IS NULL) <> (\"AcquisitionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_leasing_history_leasing_acquisitions_AccountId_AcquisitionId",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_history_leasing_frameworks_AccountId_FrameworkId",
                        columns: x => new { x.AccountId, x.FrameworkId },
                        principalTable: "leasing_frameworks",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leasing_history_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leasing_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leasing_items", x => x.Id);
                    table.CheckConstraint("CK_leasing_item_positive", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"VatPercent\" >= 0 AND \"VatPercent\" <= 100");
                    table.ForeignKey(
                        name: "FK_leasing_items_leasing_acquisitions_AccountId_AcquisitionId",
                        columns: x => new { x.AccountId, x.AcquisitionId },
                        principalTable: "leasing_acquisitions",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisitions_AccountId_FrameworkId_Status",
                table: "leasing_acquisitions",
                columns: new[] { "AccountId", "FrameworkId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisitions_FinanceOrganizationId",
                table: "leasing_acquisitions",
                column: "FinanceOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisitions_OwnerUserId",
                table: "leasing_acquisitions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_acquisitions_SupplierOrganizationId",
                table: "leasing_acquisitions",
                column: "SupplierOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_documents_AccountId_AcquisitionId",
                table: "leasing_documents",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_documents_AccountId_FrameworkId",
                table: "leasing_documents",
                columns: new[] { "AccountId", "FrameworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_documents_UploadedByUserId",
                table: "leasing_documents",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_frameworks_AccountId_Status",
                table: "leasing_frameworks",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_frameworks_FinanceOrganizationId",
                table: "leasing_frameworks",
                column: "FinanceOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_frameworks_OwnerUserId",
                table: "leasing_frameworks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_history_AccountId_AcquisitionId",
                table: "leasing_history",
                columns: new[] { "AccountId", "AcquisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_history_AccountId_FrameworkId",
                table: "leasing_history",
                columns: new[] { "AccountId", "FrameworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_history_AccountId_RecordedUtc",
                table: "leasing_history",
                columns: new[] { "AccountId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_leasing_history_ActorUserId",
                table: "leasing_history",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_leasing_items_AccountId_AcquisitionId",
                table: "leasing_items",
                columns: new[] { "AccountId", "AcquisitionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leasing_documents");

            migrationBuilder.DropTable(
                name: "leasing_history");

            migrationBuilder.DropTable(
                name: "leasing_items");

            migrationBuilder.DropTable(
                name: "leasing_acquisitions");

            migrationBuilder.DropTable(
                name: "leasing_frameworks");
        }
    }
}
