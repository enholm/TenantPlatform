using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccounts2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoginAccounts_users_UserId",
                table: "LoginAccounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LoginAccounts",
                table: "LoginAccounts");

            migrationBuilder.RenameTable(
                name: "LoginAccounts",
                newName: "login_accounts");

            migrationBuilder.RenameIndex(
                name: "IX_LoginAccounts_UserId",
                table: "login_accounts",
                newName: "IX_login_accounts_UserId");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "login_accounts",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_login_accounts",
                table: "login_accounts",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_login_accounts_Email",
                table: "login_accounts",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_login_accounts_users_UserId",
                table: "login_accounts",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_login_accounts_users_UserId",
                table: "login_accounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_login_accounts",
                table: "login_accounts");

            migrationBuilder.DropIndex(
                name: "IX_login_accounts_Email",
                table: "login_accounts");

            migrationBuilder.RenameTable(
                name: "login_accounts",
                newName: "LoginAccounts");

            migrationBuilder.RenameIndex(
                name: "IX_login_accounts_UserId",
                table: "LoginAccounts",
                newName: "IX_LoginAccounts_UserId");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "LoginAccounts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LoginAccounts",
                table: "LoginAccounts",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LoginAccounts_users_UserId",
                table: "LoginAccounts",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
