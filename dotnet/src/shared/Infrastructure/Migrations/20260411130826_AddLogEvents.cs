using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopifyProductVariantLogEvent",
                columns: table => new
                {
                    ShopifyProductVariantLogEventId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ShopifyProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopifyProductVariantLogEvent", x => x.ShopifyProductVariantLogEventId);
                    table.ForeignKey(
                        name: "FK_ShopifyProductVariantLogEvent_ShopifyProductVariant_Shopify~",
                        column: x => x.ShopifyProductVariantId,
                        principalTable: "ShopifyProductVariant",
                        principalColumn: "ShopifyProductVariantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyProductVariantLogEvent_ShopifyProductVariantId",
                table: "ShopifyProductVariantLogEvent",
                column: "ShopifyProductVariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopifyProductVariantLogEvent");
        }
    }
}
