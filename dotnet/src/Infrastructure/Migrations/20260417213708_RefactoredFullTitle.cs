using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactoredFullTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductTitle",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "VariantTitle",
                table: "ShopifyProductVariants");

            migrationBuilder.RenameColumn(
                name: "FullTitle",
                table: "ShopifyProductVariants",
                newName: "DisplayName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "ShopifyProductVariants",
                newName: "FullTitle");

            migrationBuilder.AddColumn<string>(
                name: "ProductTitle",
                table: "ShopifyProductVariants",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VariantTitle",
                table: "ShopifyProductVariants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
