using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DIP.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLiterature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Literature",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Abstract = table.Column<string>(type: "TEXT", nullable: true),
                    Doi = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    PdfUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<string>(type: "TEXT", nullable: true),
                    Authors = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Literature", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Literature_Doi",
                table: "Literature",
                column: "Doi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Literature_Source_ExternalId",
                table: "Literature",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Literature");
        }
    }
}
