using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSkulabsPropertyNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SkulabsSourceId",
                table: "SkulabsItems",
                newName: "SkulabsSourceListingId");

            migrationBuilder.RenameIndex(
                name: "IX_SkulabsItems_SkulabsSourceId",
                table: "SkulabsItems",
                newName: "IX_SkulabsItems_SkulabsSourceListingId");

            migrationBuilder.AddColumn<string>(
                name: "SkulabsSourceItemId",
                table: "SkulabsItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItems_SkulabsSourceItemId",
                table: "SkulabsItems",
                column: "SkulabsSourceItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SkulabsItems_SkulabsSourceItemId",
                table: "SkulabsItems");

            migrationBuilder.DropColumn(
                name: "SkulabsSourceItemId",
                table: "SkulabsItems");

            migrationBuilder.RenameColumn(
                name: "SkulabsSourceListingId",
                table: "SkulabsItems",
                newName: "SkulabsSourceId");

            migrationBuilder.RenameIndex(
                name: "IX_SkulabsItems_SkulabsSourceListingId",
                table: "SkulabsItems",
                newName: "IX_SkulabsItems_SkulabsSourceId");
        }
    }
}
