using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class DropReactionChannelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReactionChannelId",
                table: "BotConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReactionChannelId",
                table: "BotConfigurations",
                type: "decimal(20,0)",
                nullable: true);
        }
    }
}
