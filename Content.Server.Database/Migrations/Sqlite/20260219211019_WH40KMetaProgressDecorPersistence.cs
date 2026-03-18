using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WH40KMetaProgressDecorPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "selected_ghost_skin_id",
                table: "wh40k_meta_progress",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_ooc_name_color_id",
                table: "wh40k_meta_progress",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_ooc_title_id",
                table: "wh40k_meta_progress",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wh40k_meta_decoration_unlock",
                columns: table => new
                {
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    unlock_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    unlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    source_level = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_meta_decoration_unlock", x => new { x.player_user_id, x.unlock_id });
                    table.ForeignKey(
                        name: "FK_wh40k_meta_decoration_unlock_player_player_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_meta_decoration_unlock_player_user_id",
                table: "wh40k_meta_decoration_unlock",
                column: "player_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_meta_decoration_unlock");

            migrationBuilder.DropColumn(
                name: "selected_ghost_skin_id",
                table: "wh40k_meta_progress");

            migrationBuilder.DropColumn(
                name: "selected_ooc_name_color_id",
                table: "wh40k_meta_progress");

            migrationBuilder.DropColumn(
                name: "selected_ooc_title_id",
                table: "wh40k_meta_progress");
        }
    }
}
