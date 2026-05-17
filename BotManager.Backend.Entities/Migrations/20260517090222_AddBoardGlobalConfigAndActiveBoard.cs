using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardGlobalConfigAndActiveBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveBoardConfigurationId",
                table: "BotConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoardGlobalConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultBoardTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DefaultBoardDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultBoardDetailSubtitleLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultBoardDetailContactLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardGlobalConfigs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BoardGlobalConfigs",
                columns: new[] { "Id", "DefaultBoardDescription", "DefaultBoardDetailContactLabel", "DefaultBoardDetailSubtitleLabel", "DefaultBoardTitle" },
                values: new object[] { 1, "Celkem registrovaných skupin: {count}", "Kontakt", "Podtitul", "📋 Seznam všech skupin" });

            migrationBuilder.Sql(@"
                UPDATE bc
                SET bc.ActiveBoardConfigurationId = source.BoardConfigurationId
                FROM BotConfigurations bc
                OUTER APPLY (
                    SELECT TOP (1) bcfg.BoardConfigurationId
                    FROM BoardConfigurations bcfg
                    WHERE bcfg.BotId = bc.BotId
                    ORDER BY bcfg.BoardConfigurationId
                ) source
                WHERE bc.ActiveBoardConfigurationId IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_BotConfigurations_ActiveBoardConfigurationId",
                table: "BotConfigurations",
                column: "ActiveBoardConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BotConfigurations_BoardConfigurations_ActiveBoardConfigurationId",
                table: "BotConfigurations",
                column: "ActiveBoardConfigurationId",
                principalTable: "BoardConfigurations",
                principalColumn: "BoardConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BotConfigurations_BoardConfigurations_ActiveBoardConfigurationId",
                table: "BotConfigurations");

            migrationBuilder.DropTable(
                name: "BoardGlobalConfigs");

            migrationBuilder.DropIndex(
                name: "IX_BotConfigurations_ActiveBoardConfigurationId",
                table: "BotConfigurations");

            migrationBuilder.DropColumn(
                name: "ActiveBoardConfigurationId",
                table: "BotConfigurations");
        }
    }
}
