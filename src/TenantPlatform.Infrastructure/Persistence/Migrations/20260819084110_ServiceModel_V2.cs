using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceModel_V2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedServiceProviderOrganizationId",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "service_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "service_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "service_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "service_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedDurationMinutes",
                table: "service_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandlerType",
                table: "service_definitions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBookableByTenant",
                table: "service_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "service_definitions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                table: "service_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedServiceProviderOrganizationId",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "EstimatedDurationMinutes",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "HandlerType",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "IsBookableByTenant",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "service_definitions");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                table: "service_definitions");
        }
    }
}
