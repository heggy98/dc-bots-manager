using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class MergeBoardMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmojisMessageId",
                table: "BotConfigurations");

            migrationBuilder.RenameColumn(
                name: "TeamsMessageId",
                table: "BotConfigurations",
                newName: "BoardMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BoardMessageId",
                table: "BotConfigurations",
                newName: "TeamsMessageId");

            migrationBuilder.AddColumn<decimal>(
                name: "EmojisMessageId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);
        }
    }
}
