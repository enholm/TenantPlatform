using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementsAndDocumentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CounterpartyOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeDeadline = table.Column<DateOnly>(type: "date", nullable: true),
                    AutoRenew = table.Column<bool>(type: "boolean", nullable: false),
                    RenewalMonths = table.Column<int>(type: "integer", nullable: true),
                    Terms = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreements", x => x.Id);
                    table.UniqueConstraint("AK_agreements_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.CheckConstraint("CK_agreements_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_agreements_renewal", "\"RenewalMonths\" IS NULL OR (\"AutoRenew\" AND \"RenewalMonths\" > 0)");
                    table.CheckConstraint("CK_agreements_status", "\"Status\" IN (1,2,3,4)");
                    table.CheckConstraint("CK_agreements_type", "\"Type\" IN (1,2,3,4,5)");
                    table.CheckConstraint("CK_agreements_unit_building", "\"UnitId\" IS NULL OR \"BuildingId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_agreements_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_organizations_CounterpartyOrganizationId",
                        column: x => x.CounterpartyOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreements_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_access", x => x.Id);
                    table.CheckConstraint("CK_agreement_access_level", "\"Level\" IN (1,2)");
                    table.ForeignKey(
                        name: "FK_agreement_access_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_access_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_documents", x => x.Id);
                    table.CheckConstraint("CK_agreement_documents_category", "\"Category\" IN (1,2)");
                    table.CheckConstraint("CK_agreement_documents_size", "\"Size\" > 0");
                    table.ForeignKey(
                        name: "FK_agreement_documents_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_documents_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_access_AccountId_AgreementId_UserId",
                table: "agreement_access",
                columns: new[] { "AccountId", "AgreementId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_access_AccountId_UserId",
                table: "agreement_access",
                columns: new[] { "AccountId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_access_UserId",
                table: "agreement_access",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_documents_AccountId_AgreementId_UploadedUtc",
                table: "agreement_documents",
                columns: new[] { "AccountId", "AgreementId", "UploadedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_documents_StorageKey",
                table: "agreement_documents",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_documents_UploadedByUserId",
                table: "agreement_documents",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_NoticeDeadline",
                table: "agreements",
                columns: new[] { "AccountId", "NoticeDeadline" });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_OwnerUserId",
                table: "agreements",
                columns: new[] { "AccountId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_AccountId_Status_Type",
                table: "agreements",
                columns: new[] { "AccountId", "Status", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_BuildingId",
                table: "agreements",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_CounterpartyOrganizationId",
                table: "agreements",
                column: "CounterpartyOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_CreatedByUserId",
                table: "agreements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_OwnerUserId",
                table: "agreements",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_UnitId",
                table: "agreements",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_agreements_UpdatedByUserId",
                table: "agreements",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_access");

            migrationBuilder.DropTable(
                name: "agreement_documents");

            migrationBuilder.DropTable(
                name: "agreements");
        }
    }
}
