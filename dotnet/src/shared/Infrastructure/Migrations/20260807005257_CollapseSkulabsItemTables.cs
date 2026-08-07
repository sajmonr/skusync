using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollapseSkulabsItemTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_ShopifyProductVariantId",
                table: "SkulabsItems");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguousItemListings");

            migrationBuilder.DropTable(
                name: "SkulabsAmbiguousItems");

            migrationBuilder.DropIndex(
                name: "IX_SkulabsItems_ShopifyProductVariantId",
                table: "SkulabsItems");

            migrationBuilder.DropIndex(
                name: "IX_SkulabsItems_SkulabsSourceListingId",
                table: "SkulabsItems");

            migrationBuilder.DropColumn(
                name: "ShopifyProductVariantId",
                table: "SkulabsItems");

            migrationBuilder.DropColumn(
                name: "SkulabsSourceListingId",
                table: "SkulabsItems");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenUtc",
                table: "SkulabsItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() at time zone 'utc'");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenUtc",
                table: "SkulabsItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() at time zone 'utc'");

            migrationBuilder.CreateTable(
                name: "SkulabsItemListings",
                columns: table => new
                {
                    SkulabsItemListingId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    SkulabsItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkulabsSourceListingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RawVariantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopifyProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopifyProductVariantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsItemListings", x => x.SkulabsItemListingId);
                    table.ForeignKey(
                        name: "FK_SkulabsItemListings_ShopifyProductVariants_ShopifyProductVa~",
                        column: x => x.ShopifyProductVariantId,
                        principalTable: "ShopifyProductVariants",
                        principalColumn: "ShopifyProductVariantId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SkulabsItemListings_SkulabsItems_SkulabsItemId",
                        column: x => x.SkulabsItemId,
                        principalTable: "SkulabsItems",
                        principalColumn: "SkulabsItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItemListings_ShopifyProductVariantId",
                table: "SkulabsItemListings",
                column: "ShopifyProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItemListings_SkulabsItemId",
                table: "SkulabsItemListings",
                column: "SkulabsItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItemListings_SkulabsSourceListingId",
                table: "SkulabsItemListings",
                column: "SkulabsSourceListingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkulabsItemListings");

            migrationBuilder.DropColumn(
                name: "FirstSeenUtc",
                table: "SkulabsItems");

            migrationBuilder.DropColumn(
                name: "LastSeenUtc",
                table: "SkulabsItems");

            migrationBuilder.AddColumn<Guid>(
                name: "ShopifyProductVariantId",
                table: "SkulabsItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SkulabsSourceListingId",
                table: "SkulabsItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguousItems",
                columns: table => new
                {
                    SkulabsAmbiguousItemId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ListingCount = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SkulabsSourceItemId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Upc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkulabsAmbiguousItems", x => x.SkulabsAmbiguousItemId);
                });

            migrationBuilder.CreateTable(
                name: "SkulabsAmbiguousItemListings",
                columns: table => new
                {
                    SkulabsAmbiguousItemListingId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ShopifyProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SkulabsAmbiguousItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawVariantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopifyProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SkulabsSourceListingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItems_ShopifyProductVariantId",
                table: "SkulabsItems",
                column: "ShopifyProductVariantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItems_SkulabsSourceListingId",
                table: "SkulabsItems",
                column: "SkulabsSourceListingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItemListings_ShopifyProductVariantId",
                table: "SkulabsAmbiguousItemListings",
                column: "ShopifyProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItemListings_SkulabsAmbiguousItemId",
                table: "SkulabsAmbiguousItemListings",
                column: "SkulabsAmbiguousItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsAmbiguousItems_SkulabsSourceItemId",
                table: "SkulabsAmbiguousItems",
                column: "SkulabsSourceItemId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_ShopifyProductVariantId",
                table: "SkulabsItems",
                column: "ShopifyProductVariantId",
                principalTable: "ShopifyProductVariants",
                principalColumn: "ShopifyProductVariantId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
