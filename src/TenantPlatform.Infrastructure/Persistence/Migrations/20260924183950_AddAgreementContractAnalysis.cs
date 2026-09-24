using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementContractAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_ai_usage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_ai_usage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_ai_usage_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_ai_usage_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_analyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedAgreementRevision = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OriginalResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SchemaVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ApprovedCounterpartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedOrganizationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ApprovedAddress = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_analyses", x => x.Id);
                    table.UniqueConstraint("AK_agreement_analyses_AccountId_Id", x => new { x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_analyses_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_analyses_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_analyses_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_analyses_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_analysis_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    AgreementDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingDelete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_analysis_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_analysis_files_agreement_analyses_AccountId_Analy~",
                        columns: x => new { x.AccountId, x.AnalysisId },
                        principalTable: "agreement_analyses",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agreement_analysis_files_agreement_documents_AgreementDocum~",
                        column: x => x.AgreementDocumentId,
                        principalTable: "agreement_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OriginalStatus = table.Column<int>(type: "integer", nullable: false),
                    OriginalValue = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    AdjustedValue = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Parties = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OriginalParties = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_findings", x => x.Id);
                    table.UniqueConstraint("AK_agreement_findings_AccountId_AgreementId_Id", x => new { x.AccountId, x.AgreementId, x.Id });
                    table.ForeignKey(
                        name: "FK_agreement_findings_agreement_analyses_AccountId_AnalysisId",
                        columns: x => new { x.AccountId, x.AnalysisId },
                        principalTable: "agreement_analyses",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_findings_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_finding_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quote = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Section = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_finding_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_finding_sources_agreement_documents_AccountId_Agr~",
                        columns: x => new { x.AccountId, x.AgreementId, x.DocumentId },
                        principalTable: "agreement_documents",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_finding_sources_agreement_findings_AccountId_Agre~",
                        columns: x => new { x.AccountId, x.AgreementId, x.FindingId },
                        principalTable: "agreement_findings",
                        principalColumns: new[] { "AccountId", "AgreementId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_ai_usage_AccountId_AnalysisId_CreatedUtc",
                table: "agreement_ai_usage",
                columns: new[] { "AccountId", "AnalysisId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_ai_usage_UserId",
                table: "agreement_ai_usage",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analyses_AccountId_AgreementId",
                table: "agreement_analyses",
                columns: new[] { "AccountId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analyses_ApprovedByUserId",
                table: "agreement_analyses",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analyses_CreatedByUserId",
                table: "agreement_analyses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analyses_State_ExpiresUtc",
                table: "agreement_analyses",
                columns: new[] { "State", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analysis_files_AccountId_AnalysisId",
                table: "agreement_analysis_files",
                columns: new[] { "AccountId", "AnalysisId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analysis_files_AgreementDocumentId",
                table: "agreement_analysis_files",
                column: "AgreementDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_analysis_files_StorageKey",
                table: "agreement_analysis_files",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_finding_sources_AccountId_AgreementId_DocumentId",
                table: "agreement_finding_sources",
                columns: new[] { "AccountId", "AgreementId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_finding_sources_AccountId_AgreementId_FindingId",
                table: "agreement_finding_sources",
                columns: new[] { "AccountId", "AgreementId", "FindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_findings_AccountId_AnalysisId_Position",
                table: "agreement_findings",
                columns: new[] { "AccountId", "AnalysisId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_ai_usage");

            migrationBuilder.DropTable(
                name: "agreement_analysis_files");

            migrationBuilder.DropTable(
                name: "agreement_finding_sources");

            migrationBuilder.DropTable(
                name: "agreement_findings");

            migrationBuilder.DropTable(
                name: "agreement_analyses");
        }
    }
}
