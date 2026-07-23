using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedTitleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Title",
                table: "ShopifyProductVariant",
                newName: "ProductTitle");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ShopifyProductVariant",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "FullTitle",
                table: "ShopifyProductVariant",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VariantTitle",
                table: "ShopifyProductVariant",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullTitle",
                table: "ShopifyProductVariant");

            migrationBuilder.DropColumn(
                name: "VariantTitle",
                table: "ShopifyProductVariant");

            migrationBuilder.RenameColumn(
                name: "ProductTitle",
                table: "ShopifyProductVariant",
                newName: "Title");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ShopifyProductVariant",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
