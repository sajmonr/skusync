using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDesiredItemStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DesiredItemStates",
                columns: table => new
                {
                    DesiredItemStateId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ShopifyProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesiredItemStates", x => x.DesiredItemStateId);
                    table.ForeignKey(
                        name: "FK_DesiredItemStates_ShopifyProductVariants_ShopifyProductVari~",
                        column: x => x.ShopifyProductVariantId,
                        principalTable: "ShopifyProductVariants",
                        principalColumn: "ShopifyProductVariantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DesiredItemStates_ShopifyProductVariantId",
                table: "DesiredItemStates",
                column: "ShopifyProductVariantId",
                unique: true);

            // Seed one row per existing variant. Until now the variant row served as both the
            // Shopify mirror and the authoritative desired state, so its current values ARE the
            // desired state and the seed is exact rather than a guess.
            //
            // Location comes from the linked SkuLabs item, joined through the listing table under
            // the same single-listing-on-both-sides rule the rest of the system applies: an
            // ambiguous item has no defensible location to contribute. Unlinked variants seed "",
            // which is also what "this item has no bin" looks like — safe here because nothing
            // pushes a location until a reconcile pass has since decided one.
            migrationBuilder.Sql(
                """
                INSERT INTO "DesiredItemStates" (
                    "DesiredItemStateId", "ShopifyProductVariantId",
                    "Sku", "Barcode", "Title", "Location", "CreatedOnUtc", "UpdatedOnUtc")
                SELECT
                    uuidv7(),
                    v."ShopifyProductVariantId",
                    v."Sku",
                    v."Barcode",
                    v."DisplayName",
                    COALESCE(i."Location", ''),
                    now() at time zone 'utc',
                    now() at time zone 'utc'
                FROM "ShopifyProductVariants" v
                LEFT JOIN LATERAL (
                    SELECT si."Location"
                    FROM "SkulabsItemListings" l
                    JOIN "SkulabsItems" si ON si."SkulabsItemId" = l."SkulabsItemId"
                    WHERE l."ShopifyProductVariantId" = v."ShopifyProductVariantId"
                      AND (SELECT COUNT(*) FROM "SkulabsItemListings" x
                           WHERE x."SkulabsItemId" = l."SkulabsItemId") = 1
                      AND (SELECT COUNT(*) FROM "SkulabsItemListings" y
                           WHERE y."ShopifyProductVariantId" = v."ShopifyProductVariantId") = 1
                    LIMIT 1
                ) i ON TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DesiredItemStates");
        }
    }
}
