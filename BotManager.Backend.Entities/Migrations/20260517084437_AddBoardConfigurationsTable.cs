using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardConfigurationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoardConfigurations",
                columns: table => new
                {
                    BoardConfigurationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotId = table.Column<int>(type: "int", nullable: false),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    BoardChannelId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    BoardMessageId = table.Column<decimal>(type: "decimal(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardConfigurations", x => x.BoardConfigurationId);
                    table.ForeignKey(
                        name: "FK_BoardConfigurations_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "BotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardConfigurations_BotId",
                table: "BoardConfigurations",
                column: "BotId");

            migrationBuilder.Sql(@"
                INSERT INTO BoardConfigurations (BotId, GuildId, BoardChannelId, BoardMessageId)
                SELECT BotId, NULL, BoardChannelId, BoardMessageId
                FROM BotConfigurations
                WHERE BoardChannelId IS NOT NULL OR BoardMessageId IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "BoardChannelId",
                table: "BotConfigurations");

            migrationBuilder.DropColumn(
                name: "BoardMessageId",
                table: "BotConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BoardChannelId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoardMessageId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE bc
                SET bc.BoardChannelId = source.BoardChannelId,
                    bc.BoardMessageId = source.BoardMessageId
                FROM BotConfigurations bc
                OUTER APPLY (
                    SELECT TOP (1) bcfg.BoardChannelId, bcfg.BoardMessageId
                    FROM BoardConfigurations bcfg
                    WHERE bcfg.BotId = bc.BotId
                    ORDER BY bcfg.BoardConfigurationId
                ) source;");

            migrationBuilder.DropTable(
                name: "BoardConfigurations");
        }
    }
}
