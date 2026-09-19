using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementNoticeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CessationDate",
                table: "agreements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CurrentPeriodStartDate",
                table: "agreements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "Form",
                table: "agreements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NoticeCount",
                table: "agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticeMode",
                table: "agreements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NoticeUnit",
                table: "agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TerminationEffectiveDate",
                table: "agreements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminationNoticeCount",
                table: "agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminationNoticeUnit",
                table: "agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TerminationRegisteredByUserId",
                table: "agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TerminationRegisteredUtc",
                table: "agreements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agreement_notice_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_notice_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_notice_history_agreements_AccountId_AgreementId",
                        columns: x => new { x.AccountId, x.AgreementId },
                        principalTable: "agreements",
                        principalColumns: new[] { "AccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agreement_notice_history_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreements_TerminationRegisteredByUserId",
                table: "agreements",
                column: "TerminationRegisteredByUserId");

            // Preserve every existing manual deadline, occurrence and reminder. Incomplete
            // renewal settings remain unclassified; no duration or renewal date is invented.
            migrationBuilder.Sql("""
                UPDATE agreements SET
                    "CurrentPeriodStartDate" = "StartDate",
                    "NoticeMode" = CASE WHEN "NoticeDeadline" IS NULL THEN 0 ELSE 1 END,
                    "Form" = CASE
                        WHEN "RenewalDate" IS NOT NULL THEN 2
                        WHEN "EndDate" IS NOT NULL AND NOT "AutoRenew" AND "RenewalMonths" IS NULL THEN 1
                        ELSE 0 END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_agreements_notice_rule",
                table: "agreements",
                sql: "\"Form\" IN (0,1,2,3) AND \"NoticeMode\" IN (0,1,2) AND\n(\"Form\" = 0 OR\n (\"Form\" = 1 AND \"EndDate\" IS NOT NULL AND \"RenewalDate\" IS NULL AND NOT \"AutoRenew\" AND \"RenewalMonths\" IS NULL AND \"NoticeMode\" IN (0,1)) OR\n (\"Form\" = 2 AND \"RenewalDate\" IS NOT NULL) OR\n (\"Form\" = 3 AND \"EndDate\" IS NULL AND \"RenewalDate\" IS NULL AND NOT \"AutoRenew\" AND \"RenewalMonths\" IS NULL AND \"NoticeMode\" = 0 AND \"NoticeDeadline\" IS NULL)) AND\n(\"Form\" = 0 OR\n ((\"Form\" = 3 OR \"NoticeMode\" = 2) AND \"NoticeCount\" IS NOT NULL AND \"NoticeCount\" > 0 AND \"NoticeUnit\" IS NOT NULL AND \"NoticeUnit\" IN (1,2)) OR\n (\"Form\" <> 3 AND \"NoticeMode\" <> 2 AND \"NoticeCount\" IS NULL AND \"NoticeUnit\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_agreements_termination",
                table: "agreements",
                sql: "(\"TerminationEffectiveDate\" IS NULL AND \"CessationDate\" IS NULL AND \"TerminationNoticeCount\" IS NULL AND \"TerminationNoticeUnit\" IS NULL AND \"TerminationRegisteredUtc\" IS NULL AND \"TerminationRegisteredByUserId\" IS NULL) OR\n(\"Form\" = 3 AND \"TerminationEffectiveDate\" IS NOT NULL AND \"CessationDate\" IS NOT NULL AND \"TerminationNoticeCount\" IS NOT NULL AND \"TerminationNoticeCount\" > 0 AND \"TerminationNoticeUnit\" IS NOT NULL AND \"TerminationNoticeUnit\" IN (1,2) AND \"TerminationRegisteredUtc\" IS NOT NULL AND \"TerminationRegisteredByUserId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_notice_history_AccountId_AgreementId_CreatedUtc",
                table: "agreement_notice_history",
                columns: new[] { "AccountId", "AgreementId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_notice_history_ActorUserId",
                table: "agreement_notice_history",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_agreements_users_TerminationRegisteredByUserId",
                table: "agreements",
                column: "TerminationRegisteredByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agreements_users_TerminationRegisteredByUserId",
                table: "agreements");

            migrationBuilder.DropTable(
                name: "agreement_notice_history");

            migrationBuilder.DropIndex(
                name: "IX_agreements_TerminationRegisteredByUserId",
                table: "agreements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_agreements_notice_rule",
                table: "agreements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_agreements_termination",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "CessationDate",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "CurrentPeriodStartDate",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "Form",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "NoticeCount",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "NoticeMode",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "NoticeUnit",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "TerminationEffectiveDate",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "TerminationNoticeCount",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "TerminationNoticeUnit",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "TerminationRegisteredByUserId",
                table: "agreements");

            migrationBuilder.DropColumn(
                name: "TerminationRegisteredUtc",
                table: "agreements");
        }
    }
}
