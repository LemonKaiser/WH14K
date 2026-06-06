using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WH40KMuteSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_mute",
                columns: table => new
                {
                    wh40k_mute_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mute_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_mute", x => x.wh40k_mute_id);
                    table.ForeignKey(
                        name: "FK_wh40k_mute_player_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_wh40k_mute_player_player_user_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_unmute",
                columns: table => new
                {
                    wh40k_unmute_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mute_id = table.Column<int>(type: "integer", nullable: false),
                    unmuting_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unmute_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_unmute", x => x.wh40k_unmute_id);
                    table.ForeignKey(
                        name: "FK_wh40k_unmute_player_unmuting_admin_id",
                        column: x => x.unmuting_admin_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_wh40k_unmute_wh40k_mute_mute_id",
                        column: x => x.mute_id,
                        principalTable: "wh40k_mute",
                        principalColumn: "wh40k_mute_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_mute_created_by_id",
                table: "wh40k_mute",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_mute_player_user_id",
                table: "wh40k_mute",
                column: "player_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_mute_player_user_id_type",
                table: "wh40k_mute",
                columns: new[] { "player_user_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_unmute_mute_id",
                table: "wh40k_unmute",
                column: "mute_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_unmute_unmuting_admin_id",
                table: "wh40k_unmute",
                column: "unmuting_admin_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_unmute");

            migrationBuilder.DropTable(
                name: "wh40k_mute");
        }
    }
}
