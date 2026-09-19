using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementFinancialLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "agreements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_agreement_documents_AccountId_AgreementId_Id",
                table: "agreement_documents",
                columns: new[] { "AccountId", "AgreementId", "Id" });

            migrationBuilder.CreateTable(
                name: "agreement_delivery_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_delivery_groups", x => x.Id);
                    table.UniqueConstraint("AK_agreement_delivery_groups_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_delivery_groups_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_delivery_groups_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_lines", x => x.Id);
                    table.UniqueConstraint("AK_agreement_lines_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_lines_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_line_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FirstPayableDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PayableSourceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayableSourceStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PayableOffsetMonths = table.Column<int>(type: "integer", nullable: true),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Anchor = table.Column<int>(type: "integer", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BillingTiming = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeliveryGroupId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_line_versions", x => x.Id);
                    table.UniqueConstraint("AK_agreement_line_versions_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_line_versions_agreement_delivery_groups_AccountId~",
                        columns: x => new { x.AccountId, x.AgreementId, x.DeliveryGroupId },
                        principalTable: "agreement_delivery_groups",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_line_versions_agreement_lines_AccountId_Agreement~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_line_versions_agreement_lines_AccountId_Agreemen~1",
                        columns: x => new { x.AccountId, x.AgreementId, x.PayableSourceLineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_line_versions_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_price_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_price_versions", x => x.Id);
                    table.CheckConstraint("CK_agreement_price_positive", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_agreement_price_versions_agreement_lines_AccountId_Agreemen~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_price_versions_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_line_documents",
                columns: table => new
                {
                    LineVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_line_documents", x => new { x.LineVersionId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_agreement_line_documents_agreement_documents_AccountId_Agre~",
                        columns: x => new { x.AccountId, x.AgreementId, x.DocumentId },
                        principalTable: "agreement_documents",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_line_documents_agreement_line_versions_AccountId_~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineVersionId },
                        principalTable: "agreement_line_versions",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_delivery_groups_CreatedByUserId",
                table: "agreement_delivery_groups",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_documents_AccountId_AgreementId_DocumentId",
                table: "agreement_line_documents",
                columns: new[] { "AccountId", "AgreementId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_documents_AccountId_AgreementId_LineVersionId",
                table: "agreement_line_documents",
                columns: new[] { "AccountId", "AgreementId", "LineVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_versions_AccountId_AgreementId_DeliveryGroup~",
                table: "agreement_line_versions",
                columns: new[] { "AccountId", "AgreementId", "DeliveryGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_versions_AccountId_AgreementId_LineId",
                table: "agreement_line_versions",
                columns: new[] { "AccountId", "AgreementId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_versions_AccountId_AgreementId_PayableSource~",
                table: "agreement_line_versions",
                columns: new[] { "AccountId", "AgreementId", "PayableSourceLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_versions_AccountId_LineId_Sequence",
                table: "agreement_line_versions",
                columns: new[] { "AccountId", "LineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_line_versions_ActorUserId",
                table: "agreement_line_versions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_price_versions_AccountId_AgreementId_LineId",
                table: "agreement_price_versions",
                columns: new[] { "AccountId", "AgreementId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_price_versions_AccountId_LineId_Sequence",
                table: "agreement_price_versions",
                columns: new[] { "AccountId", "LineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_price_versions_ActorUserId",
                table: "agreement_price_versions",
                column: "ActorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_line_documents");

            migrationBuilder.DropTable(
                name: "agreement_price_versions");

            migrationBuilder.DropTable(
                name: "agreement_line_versions");

            migrationBuilder.DropTable(
                name: "agreement_delivery_groups");

            migrationBuilder.DropTable(
                name: "agreement_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_agreement_documents_AccountId_AgreementId_Id",
                table: "agreement_documents");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "agreements");
        }
    }
}
