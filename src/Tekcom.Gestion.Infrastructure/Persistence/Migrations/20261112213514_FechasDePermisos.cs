using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tekcom.Gestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FechasDePermisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ElectricalApprovedOn",
                table: "Sites",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ElectricalRequestedOn",
                table: "Sites",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MunicipalApprovedOn",
                table: "Sites",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MunicipalRequestedOn",
                table: "Sites",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElectricalApprovedOn",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ElectricalRequestedOn",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "MunicipalApprovedOn",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "MunicipalRequestedOn",
                table: "Sites");
        }
    }
}
