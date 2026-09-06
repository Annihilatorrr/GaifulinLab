using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_articles_created_at",
                table: "articles");

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "articles",
                type: "text",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_articles_owner_user_id_updated_at",
                table: "articles",
                columns: new[] { "OwnerUserId", "UpdatedAt" });

            migrationBuilder.AddForeignKey(
                name: "fk_articles_owner_user_id",
                table: "articles",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_articles_owner_user_id",
                table: "articles");

            migrationBuilder.DropIndex(
                name: "ix_articles_owner_user_id_updated_at",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "articles");

            migrationBuilder.CreateIndex(
                name: "ix_articles_created_at",
                table: "articles",
                column: "CreatedAt");
        }
    }
}
