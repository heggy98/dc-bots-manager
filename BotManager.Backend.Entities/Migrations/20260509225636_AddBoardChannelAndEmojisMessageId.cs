using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardChannelAndEmojisMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BoardChannelId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmojisMessageId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardChannelId",
                table: "BotConfigurations");

            migrationBuilder.DropColumn(
                name: "EmojisMessageId",
                table: "BotConfigurations");
        }
    }
}
