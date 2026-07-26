using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropSkulabsAmbiguityReasonAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityReasons_Reason",
                table: "SkulabsAmbiguousItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityStatuses_Status",
                table: "SkulabsAmbiguousItems");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguityReasons");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguityStatuses");

            migrationBuilder.DropIndex(
                name: "IX_SkulabsAmbiguousItems_Reason",
                table: "SkulabsAmbiguousItems");

            migrationBuilder.DropIndex(
                name: "IX_SkulabsAmbiguousItems_Status",
                table: "SkulabsAmbiguousItems");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SkulabsAmbiguousItems");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SkulabsAmbiguousItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Reason",
                table: "SkulabsAmbiguousItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SkulabsAmbiguousItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguityReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsAmbiguityReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguityStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsAmbiguityStatuses", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SkulabsAmbiguityReasons",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "NoListings" },
                    { 2, "MultipleListings" },
                    { 3, "ListingNotInShopify" }
                });

            migrationBuilder.InsertData(
                table: "SkulabsAmbiguityStatuses",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Unresolved" },
                    { 2, "Resolved" },
                    { 3, "Ignored" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_Reason",
                table: "SkulabsAmbiguousItems",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_Status",
                table: "SkulabsAmbiguousItems",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityReasons_Reason",
                table: "SkulabsAmbiguousItems",
                column: "Reason",
                principalTable: "SkulabsAmbiguityReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityStatuses_Status",
                table: "SkulabsAmbiguousItems",
                column: "Status",
                principalTable: "SkulabsAmbiguityStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
