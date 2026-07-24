using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIsDeletedAndDeletedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedOn",
                table: "ShopifyProductVariants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "'-infinity'::timestamp");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ShopifyProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants",
                column: "IsActive",
                filter: "\"IsActive\" = true AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "DeletedOn",
                table: "ShopifyProductVariants");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ShopifyProductVariants");

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyProductVariants_IsActive",
                table: "ShopifyProductVariants",
                column: "IsActive",
                filter: "\"IsActive\" = true");
        }
    }
}
