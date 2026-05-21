using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSkulabsToVariantRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_SkulabsItemId",
                table: "SkulabsItems");

            migrationBuilder.CreateIndex(
                name: "IX_SkulabsItems_ShopifyProductVariantId",
                table: "SkulabsItems",
                column: "ShopifyProductVariantId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_ShopifyProductVariantId",
                table: "SkulabsItems",
                column: "ShopifyProductVariantId",
                principalTable: "ShopifyProductVariants",
                principalColumn: "ShopifyProductVariantId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_ShopifyProductVariantId",
                table: "SkulabsItems");

            migrationBuilder.DropIndex(
                name: "IX_SkulabsItems_ShopifyProductVariantId",
                table: "SkulabsItems");

            migrationBuilder.AddForeignKey(
                name: "FK_SkulabsItems_ShopifyProductVariants_SkulabsItemId",
                table: "SkulabsItems",
                column: "SkulabsItemId",
                principalTable: "ShopifyProductVariants",
                principalColumn: "ShopifyProductVariantId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
