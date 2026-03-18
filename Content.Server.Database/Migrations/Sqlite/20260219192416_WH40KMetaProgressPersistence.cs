using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WH40KMetaProgressPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_meta_achievement_progress",
                columns: table => new
                {
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    achievement_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    progress_value = table.Column<int>(type: "INTEGER", nullable: false),
                    unlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    claimed = table.Column<bool>(type: "INTEGER", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_meta_achievement_progress", x => new { x.player_user_id, x.achievement_id });
                    table.ForeignKey(
                        name: "FK_wh40k_meta_achievement_progress_player_player_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_meta_progress",
                columns: table => new
                {
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    lifetime_xp = table.Column<int>(type: "INTEGER", nullable: false),
                    season_xp = table.Column<int>(type: "INTEGER", nullable: false),
                    last_progress_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_meta_progress", x => x.player_user_id);
                    table.ForeignKey(
                        name: "FK_wh40k_meta_progress_player_player_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_meta_achievement_progress_player_user_id",
                table: "wh40k_meta_achievement_progress",
                column: "player_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_meta_achievement_progress");

            migrationBuilder.DropTable(
                name: "wh40k_meta_progress");
        }
    }
}
