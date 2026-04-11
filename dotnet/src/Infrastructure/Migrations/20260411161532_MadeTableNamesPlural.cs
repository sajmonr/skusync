using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MadeTableNamesPlural : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShopifyProductVariantLogEvent_ShopifyProductVariant_Shopify~",
                table: "ShopifyProductVariantLogEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShopifyProductVariantLogEvent",
                table: "ShopifyProductVariantLogEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShopifyProductVariant",
                table: "ShopifyProductVariant");

            migrationBuilder.RenameTable(
                name: "ShopifyProductVariantLogEvent",
                newName: "ShopifyProductVariantLogEvents");

            migrationBuilder.RenameTable(
                name: "ShopifyProductVariant",
                newName: "ShopifyProductVariants");

            migrationBuilder.RenameIndex(
                name: "IX_ShopifyProductVariantLogEvent_ShopifyProductVariantId",
                table: "ShopifyProductVariantLogEvents",
                newName: "IX_ShopifyProductVariantLogEvents_ShopifyProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_ShopifyProductVariant_GlobalVariantId",
                table: "ShopifyProductVariants",
                newName: "IX_ShopifyProductVariants_GlobalVariantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShopifyProductVariantLogEvents",
                table: "ShopifyProductVariantLogEvents",
                column: "ShopifyProductVariantLogEventId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShopifyProductVariants",
                table: "ShopifyProductVariants",
                column: "ShopifyProductVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShopifyProductVariantLogEvents_ShopifyProductVariants_Shopi~",
                table: "ShopifyProductVariantLogEvents",
                column: "ShopifyProductVariantId",
                principalTable: "ShopifyProductVariants",
                principalColumn: "ShopifyProductVariantId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShopifyProductVariantLogEvents_ShopifyProductVariants_Shopi~",
                table: "ShopifyProductVariantLogEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShopifyProductVariants",
                table: "ShopifyProductVariants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShopifyProductVariantLogEvents",
                table: "ShopifyProductVariantLogEvents");

            migrationBuilder.RenameTable(
                name: "ShopifyProductVariants",
                newName: "ShopifyProductVariant");

            migrationBuilder.RenameTable(
                name: "ShopifyProductVariantLogEvents",
                newName: "ShopifyProductVariantLogEvent");

            migrationBuilder.RenameIndex(
                name: "IX_ShopifyProductVariants_GlobalVariantId",
                table: "ShopifyProductVariant",
                newName: "IX_ShopifyProductVariant_GlobalVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_ShopifyProductVariantLogEvents_ShopifyProductVariantId",
                table: "ShopifyProductVariantLogEvent",
                newName: "IX_ShopifyProductVariantLogEvent_ShopifyProductVariantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShopifyProductVariant",
                table: "ShopifyProductVariant",
                column: "ShopifyProductVariantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShopifyProductVariantLogEvent",
                table: "ShopifyProductVariantLogEvent",
                column: "ShopifyProductVariantLogEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShopifyProductVariantLogEvent_ShopifyProductVariant_Shopify~",
                table: "ShopifyProductVariantLogEvent",
                column: "ShopifyProductVariantId",
                principalTable: "ShopifyProductVariant",
                principalColumn: "ShopifyProductVariantId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
