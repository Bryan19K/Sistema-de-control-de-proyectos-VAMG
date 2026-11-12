using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tekcom.Gestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermisosMunicipales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MunicipalPermitStatus",
                table: "Sites",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MunicipalPermitStatus",
                table: "Sites");
        }
    }
}
