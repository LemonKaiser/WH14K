using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WH40KDiscordAuthPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_discord_link",
                columns: table => new
                {
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discord_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    global_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    avatar_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    access_token = table.Column<string>(type: "text", nullable: false),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    token_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_refresh_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    guild_id_cached = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_guild_refresh_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    guild_member_cached = table.Column<bool>(type: "boolean", nullable: false),
                    guild_nickname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    role_cache_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_discord_link", x => x.player_user_id);
                    table.ForeignKey(
                        name: "FK_wh40k_discord_link_player_player_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_discord_link_discord_user_id",
                table: "wh40k_discord_link",
                column: "discord_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_discord_link");
        }
    }
}
