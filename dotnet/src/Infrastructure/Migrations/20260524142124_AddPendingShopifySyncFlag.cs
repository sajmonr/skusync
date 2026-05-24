using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingShopifySyncFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PendingShopifySync",
                table: "ShopifyProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyProductVariants_PendingShopifySync",
                table: "ShopifyProductVariants",
                column: "PendingShopifySync",
                filter: "\"PendingShopifySync\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopifyProductVariants_PendingShopifySync",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "PendingShopifySync",
                table: "ShopifyProductVariants");
        }
    }
}
