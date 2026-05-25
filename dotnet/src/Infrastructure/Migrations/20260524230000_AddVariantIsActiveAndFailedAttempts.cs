using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIsActiveAndFailedAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedShopifySyncAttempts",
                table: "ShopifyProductVariants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ShopifyProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants",
                column: "IsActive",
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "FailedShopifySyncAttempts",
                table: "ShopifyProductVariants");
        }
    }
}
