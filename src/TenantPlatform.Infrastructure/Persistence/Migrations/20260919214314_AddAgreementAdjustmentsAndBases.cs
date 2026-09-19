using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementAdjustmentsAndBases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdjustmentId",
                table: "agreement_price_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Independent",
                table: "agreement_price_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_agreement_price_versions_AccountId_AgreementId_Id",
                table: "agreement_price_versions",
                columns: new[] { "AccountId", "AgreementId", "Id" });

            migrationBuilder.CreateTable(
                name: "agreement_bases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginalBasisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    GeneratedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_bases", x => x.Id);
                    table.UniqueConstraint("AK_agreement_bases_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.CheckConstraint("CK_basis_direction", "\"Direction\" IN (1,2)");
                    table.ForeignKey(
                        name: "FK_agreement_bases_agreement_bases_AccountId_AgreementId_Origi~",
                        columns: x => new { x.AccountId, x.AgreementId, x.OriginalBasisId },
                        principalTable: "agreement_bases",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_bases_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_bases_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_bases_users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_bases_users_GeneratedByUserId",
                        column: x => x.GeneratedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_indices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Source = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Resolution = table.Column<int>(type: "integer", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_indices", x => x.Id);
                    table.UniqueConstraint("AK_agreement_indices_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_indices_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_indices_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_basis_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasisId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_basis_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_basis_events_agreement_bases_AccountId_AgreementI~",
                        columns: x => new { x.AccountId, x.AgreementId, x.BasisId },
                        principalTable: "agreement_bases",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_basis_events_agreement_lines_AccountId_AgreementI~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_basis_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_basis_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_basis_snapshots_agreement_bases_AccountId_Agreeme~",
                        columns: x => new { x.AccountId, x.AgreementId, x.BasisId },
                        principalTable: "agreement_bases",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_basis_snapshots_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_adjustment_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    IndexId = table.Column<Guid>(type: "uuid", nullable: true),
                    SharePercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Addition = table.Column<int>(type: "integer", nullable: false),
                    AdditionPercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    FixedPercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    FloorPercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    CeilingPercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AllowDecrease = table.Column<bool>(type: "boolean", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    BasePriceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseIndexPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstAllowedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    Anchor = table.Column<int>(type: "integer", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ComparisonOffsetMonths = table.Column<int>(type: "integer", nullable: false),
                    PriceTiming = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_adjustment_rules", x => x.Id);
                    table.UniqueConstraint("AK_agreement_adjustment_rules_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_rules_agreement_indices_AccountId_Inde~",
                        columns: x => new { x.AccountId, x.IndexId },
                        principalTable: "agreement_indices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_rules_agreement_lines_AccountId_Agreem~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_rules_agreement_price_versions_Account~",
                        columns: x => new { x.AccountId, x.AgreementId, x.BasePriceVersionId },
                        principalTable: "agreement_price_versions",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_rules_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_index_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: false),
                    PublishedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_index_values", x => x.Id);
                    table.CheckConstraint("CK_index_value_positive", "\"Value\" > 0");
                    table.ForeignKey(
                        name: "FK_agreement_index_values_agreement_indices_AccountId_IndexId",
                        columns: x => new { x.AccountId, x.IndexId },
                        principalTable: "agreement_indices",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_index_values_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_adjustment_documents",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_adjustment_documents", x => new { x.RuleId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_documents_agreement_adjustment_rules_A~",
                        columns: x => new { x.AccountId, x.AgreementId, x.RuleId },
                        principalTable: "agreement_adjustment_rules",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_documents_agreement_documents_AccountI~",
                        columns: x => new { x.AccountId, x.AgreementId, x.DocumentId },
                        principalTable: "agreement_documents",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_adjustment_proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChosenPercent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    OldPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NewPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_adjustment_proposals", x => x.Id);
                    table.UniqueConstraint("AK_agreement_adjustment_proposals_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_proposals_agreement_adjustment_rules_A~",
                        columns: x => new { x.AccountId, x.AgreementId, x.RuleId },
                        principalTable: "agreement_adjustment_rules",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_proposals_agreement_lines_AccountId_Ag~",
                        columns: x => new { x.AccountId, x.AgreementId, x.LineId },
                        principalTable: "agreement_lines",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_proposals_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_adjustment_proposals_users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_price_versions_AccountId_AgreementId_AdjustmentId",
                table: "agreement_price_versions",
                columns: new[] { "AccountId", "AgreementId", "AdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_documents_AccountId_AgreementId_Docume~",
                table: "agreement_adjustment_documents",
                columns: new[] { "AccountId", "AgreementId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_documents_AccountId_AgreementId_RuleId",
                table: "agreement_adjustment_documents",
                columns: new[] { "AccountId", "AgreementId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_proposals_AccountId_AgreementId_LineId",
                table: "agreement_adjustment_proposals",
                columns: new[] { "AccountId", "AgreementId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_proposals_AccountId_AgreementId_RuleId",
                table: "agreement_adjustment_proposals",
                columns: new[] { "AccountId", "AgreementId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_proposals_AccountId_LineId_ScheduledDa~",
                table: "agreement_adjustment_proposals",
                columns: new[] { "AccountId", "LineId", "ScheduledDate" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_proposals_ActorUserId",
                table: "agreement_adjustment_proposals",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_proposals_DecidedByUserId",
                table: "agreement_adjustment_proposals",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_rules_AccountId_AgreementId_BasePriceV~",
                table: "agreement_adjustment_rules",
                columns: new[] { "AccountId", "AgreementId", "BasePriceVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_rules_AccountId_AgreementId_LineId",
                table: "agreement_adjustment_rules",
                columns: new[] { "AccountId", "AgreementId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_rules_AccountId_IndexId",
                table: "agreement_adjustment_rules",
                columns: new[] { "AccountId", "IndexId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_rules_AccountId_LineId_EffectiveFrom",
                table: "agreement_adjustment_rules",
                columns: new[] { "AccountId", "LineId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_adjustment_rules_ActorUserId",
                table: "agreement_adjustment_rules",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_bases_AccountId_AgreementId_OriginalBasisId",
                table: "agreement_bases",
                columns: new[] { "AccountId", "AgreementId", "OriginalBasisId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_bases_AccountId_OriginalBasisId",
                table: "agreement_bases",
                columns: new[] { "AccountId", "OriginalBasisId" },
                unique: true,
                filter: "\"OriginalBasisId\" IS NOT NULL AND \"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_bases_ApprovedByUserId",
                table: "agreement_bases",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_bases_CancelledByUserId",
                table: "agreement_bases",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_bases_GeneratedByUserId",
                table: "agreement_bases",
                column: "GeneratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_events_AccountId_AgreementId_BasisId",
                table: "agreement_basis_events",
                columns: new[] { "AccountId", "AgreementId", "BasisId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_events_AccountId_AgreementId_LineId",
                table: "agreement_basis_events",
                columns: new[] { "AccountId", "AgreementId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_events_AccountId_LineId_EventKey",
                table: "agreement_basis_events",
                columns: new[] { "AccountId", "LineId", "EventKey" },
                unique: true,
                filter: "\"Active\"");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_snapshots_AccountId_AgreementId_BasisId",
                table: "agreement_basis_snapshots",
                columns: new[] { "AccountId", "AgreementId", "BasisId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_snapshots_AccountId_BasisId_Revision",
                table: "agreement_basis_snapshots",
                columns: new[] { "AccountId", "BasisId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_basis_snapshots_ActorUserId",
                table: "agreement_basis_snapshots",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_index_values_AccountId_IndexId_Period_Revision",
                table: "agreement_index_values",
                columns: new[] { "AccountId", "IndexId", "Period", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_index_values_ActorUserId",
                table: "agreement_index_values",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_indices_AccountId_Code",
                table: "agreement_indices",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_indices_ActorUserId",
                table: "agreement_indices",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_agreement_price_versions_agreement_adjustment_proposals_Acc~",
                table: "agreement_price_versions",
                columns: new[] { "AccountId", "AgreementId", "AdjustmentId" },
                principalTable: "agreement_adjustment_proposals",
                principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agreement_price_versions_agreement_adjustment_proposals_Acc~",
                table: "agreement_price_versions");

            migrationBuilder.DropTable(
                name: "agreement_adjustment_documents");

            migrationBuilder.DropTable(
                name: "agreement_adjustment_proposals");

            migrationBuilder.DropTable(
                name: "agreement_basis_events");

            migrationBuilder.DropTable(
                name: "agreement_basis_snapshots");

            migrationBuilder.DropTable(
                name: "agreement_index_values");

            migrationBuilder.DropTable(
                name: "agreement_adjustment_rules");

            migrationBuilder.DropTable(
                name: "agreement_bases");

            migrationBuilder.DropTable(
                name: "agreement_indices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_agreement_price_versions_AccountId_AgreementId_Id",
                table: "agreement_price_versions");

            migrationBuilder.DropIndex(
                name: "IX_agreement_price_versions_AccountId_AgreementId_AdjustmentId",
                table: "agreement_price_versions");

            migrationBuilder.DropColumn(
                name: "AdjustmentId",
                table: "agreement_price_versions");

            migrationBuilder.DropColumn(
                name: "Independent",
                table: "agreement_price_versions");
        }
    }
}
