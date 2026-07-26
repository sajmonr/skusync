using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkulabsAmbiguousItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguousItems",
                columns: table => new
                {
                    SkulabsAmbiguousItemId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    SkulabsSourceItemId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Upc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ListingCount = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsAmbiguousItems", x => x.SkulabsAmbiguousItemId);
                    table.ForeignKey(
                        name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityReasons_Reason",
                        column: x => x.Reason,
                        principalTable: "SkulabsAmbiguityReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkulabsAmbiguousItems_SkulabsAmbiguityStatuses_Status",
                        column: x => x.Status,
                        principalTable: "SkulabsAmbiguityStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguousItemListings",
                columns: table => new
                {
                    SkulabsAmbiguousItemListingId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    SkulabsAmbiguousItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkulabsSourceListingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RawVariantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopifyProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopifyProductVariantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsAmbiguousItemListings", x => x.SkulabsAmbiguousItemListingId);
                    table.ForeignKey(
                        name: "FK_SkulabsAmbiguousItemListings_ShopifyProductVariants_Shopify~",
                        column: x => x.ShopifyProductVariantId,
                        principalTable: "ShopifyProductVariants",
                        principalColumn: "ShopifyProductVariantId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SkulabsAmbiguousItemListings_SkulabsAmbiguousItems_SkulabsA~",
                        column: x => x.SkulabsAmbiguousItemId,
                        principalTable: "SkulabsAmbiguousItems",
                        principalColumn: "SkulabsAmbiguousItemId",
                        onDelete: ReferentialAction.Cascade);
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
                name: "IX_SkulabsAmbiguousItemListings_ShopifyProductVariantId",
                table: "SkulabsAmbiguousItemListings",
                column: "ShopifyProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItemListings_SkulabsAmbiguousItemId",
                table: "SkulabsAmbiguousItemListings",
                column: "SkulabsAmbiguousItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_Reason",
                table: "SkulabsAmbiguousItems",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_SkulabsSourceItemId",
                table: "SkulabsAmbiguousItems",
                column: "SkulabsSourceItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_Status",
                table: "SkulabsAmbiguousItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkulabsAmbiguousItemListings");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguousItems");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguityReasons");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguityStatuses");
        }
    }
}
