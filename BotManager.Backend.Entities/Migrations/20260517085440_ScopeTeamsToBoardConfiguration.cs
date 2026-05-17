using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class ScopeTeamsToBoardConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoardConfigurationId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                INSERT INTO BoardConfigurations (BotId, BoardType)
                SELECT b.BotId, 'teams'
                FROM Bots b
                LEFT JOIN BoardConfigurations bc ON bc.BotId = b.BotId
                WHERE bc.BotId IS NULL;");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM Teams WHERE BoardConfigurationId IS NULL)
                BEGIN
                    INSERT INTO Teams (Name, LeaderName, CommanderContact, Emoji, BoardConfigurationId)
                    SELECT t.Name,
                           t.LeaderName,
                           t.CommanderContact,
                           t.Emoji,
                           bc.BoardConfigurationId
                    FROM Teams t
                    CROSS JOIN BoardConfigurations bc
                    WHERE t.BoardConfigurationId IS NULL;

                    DELETE FROM Teams
                    WHERE BoardConfigurationId IS NULL;
                END;");

            migrationBuilder.Sql(@"
                ALTER TABLE Teams
                ALTER COLUMN BoardConfigurationId int NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_BoardConfigurationId",
                table: "Teams",
                column: "BoardConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_BoardConfigurations_BoardConfigurationId",
                table: "Teams",
                column: "BoardConfigurationId",
                principalTable: "BoardConfigurations",
                principalColumn: "BoardConfigurationId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_BoardConfigurations_BoardConfigurationId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Teams_BoardConfigurationId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "BoardConfigurationId",
                table: "Teams");
        }
    }
}
