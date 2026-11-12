using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tekcom.Gestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermisosDeNodo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PermitStatus",
                table: "Sites",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Lo que ya traía el Excel de asignación ("APROBADO", "EN TRAMITE"…) pasa al estado nuevo.
            migrationBuilder.Sql("UPDATE \"Sites\" SET \"PermitStatus\" = 2 WHERE UPPER(\"ElectricalPermitStatus\") LIKE '%APROB%';");
            migrationBuilder.Sql("UPDATE \"Sites\" SET \"PermitStatus\" = 1 WHERE \"PermitStatus\" = 0 AND (UPPER(\"ElectricalPermitStatus\") LIKE '%TRAMIT%' OR UPPER(\"ElectricalPermitStatus\") LIKE '%TRÁMIT%' OR UPPER(\"ElectricalPermitStatus\") LIKE '%PROCESO%');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermitStatus",
                table: "Sites");
        }
    }
}
