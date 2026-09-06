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
                nullable: true);

            // Legacy articles predate user ownership. They belong to the single existing administrator.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    owner_id text;
                    owner_count integer;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM articles) THEN
                        RETURN;
                    END IF;

                    SELECT COUNT(*), MIN(u."Id")
                    INTO owner_count, owner_id
                    FROM "AspNetUsers" AS u
                    INNER JOIN "AspNetUserRoles" AS ur ON ur."UserId" = u."Id"
                    INNER JOIN "AspNetRoles" AS r ON r."Id" = ur."RoleId"
                    WHERE r."NormalizedName" = 'ADMIN';

                    IF owner_count <> 1 THEN
                        RAISE EXCEPTION
                            'Cannot assign existing articles: exactly one Admin user is required, found %.',
                            owner_count;
                    END IF;

                    UPDATE articles
                    SET "OwnerUserId" = owner_id
                    WHERE "OwnerUserId" IS NULL;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerUserId",
                table: "articles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

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
