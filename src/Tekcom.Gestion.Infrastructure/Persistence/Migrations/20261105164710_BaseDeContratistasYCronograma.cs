using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tekcom.Gestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaseDeContratistasYCronograma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Contractors",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "Contractors",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Contractors",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bank",
                table: "Contractors",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankHolder",
                table: "Contractors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrewLeaderName",
                table: "Contractors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrewLeaderPhone",
                table: "Contractors",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Crews",
                table: "Contractors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dui",
                table: "Contractors",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Contractors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalRepresentative",
                table: "Contractors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Contractors",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nrc",
                table: "Contractors",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderType",
                table: "Contractors",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "Contractors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorName",
                table: "Contractors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorPhone",
                table: "Contractors",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractorPeople",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContractorId = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Dui = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Position = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    InsuranceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    InsuranceStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Insurer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PolicyNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CoverageEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractorPeople_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalTable: "Contractors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorPeople_ContractorId",
                table: "ContractorPeople",
                column: "ContractorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractorPeople");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Bank",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "BankHolder",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "CrewLeaderName",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "CrewLeaderPhone",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Crews",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Dui",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "LegalRepresentative",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Nrc",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "ProviderType",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "SupervisorName",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "SupervisorPhone",
                table: "Contractors");
        }
    }
}
