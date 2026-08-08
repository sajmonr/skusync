using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantRawTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductTitle",
                table: "ShopifyProductVariants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VariantTitle",
                table: "ShopifyProductVariants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            // Existing rows only hold the composed display name, and "Product (Variant)" cannot be
            // split back apart reliably — either part may contain brackets of its own. Seeding the
            // product title with it and leaving the variant title empty is the honest approximation:
            // the next webhook or import overwrites both with what Shopify actually sent. Nothing
            // reads these until a SKU has to be generated, and every existing row already has one.
            migrationBuilder.Sql(
                """
                UPDATE "ShopifyProductVariants" SET "ProductTitle" = "DisplayName";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductTitle",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "VariantTitle",
                table: "ShopifyProductVariants");
        }
    }
}
