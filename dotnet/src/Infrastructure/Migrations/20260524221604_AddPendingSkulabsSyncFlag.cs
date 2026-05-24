using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingSkulabsSyncFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PendingSkulabsSync",
                table: "SkulabsItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItems_PendingSkulabsSync",
                table: "SkulabsItems",
                column: "PendingSkulabsSync",
                filter: "\"PendingSkulabsSync\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SkulabsItems_PendingSkulabsSync",
                table: "SkulabsItems");

            migrationBuilder.DropColumn(
                name: "PendingSkulabsSync",
                table: "SkulabsItems");
        }
    }
}
