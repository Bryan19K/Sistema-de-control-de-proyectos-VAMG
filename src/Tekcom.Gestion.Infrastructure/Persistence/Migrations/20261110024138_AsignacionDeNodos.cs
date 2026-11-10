using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tekcom.Gestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsignacionDeNodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AtpDelivered",
                table: "Sites",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElectricalPermitStatus",
                table: "Sites",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomesPassed",
                table: "Sites",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KmzStatus",
                table: "Sites",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaterialsDelivered",
                table: "Sites",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhotoReportStatus",
                table: "Sites",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UmConsolidatedStatus",
                table: "Sites",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtpDelivered",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ElectricalPermitStatus",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "HomesPassed",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "KmzStatus",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "MaterialsDelivered",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "PhotoReportStatus",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "UmConsolidatedStatus",
                table: "Sites");
        }
    }
}
