using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardConfigurationContentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardDescriptionTemplate",
                table: "BoardConfigurations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardTitle",
                table: "BoardConfigurations",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardType",
                table: "BoardConfigurations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "teams");

            migrationBuilder.AddColumn<string>(
                name: "ContactLabel",
                table: "BoardConfigurations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtitleLabel",
                table: "BoardConfigurations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardDescriptionTemplate",
                table: "BoardConfigurations");

            migrationBuilder.DropColumn(
                name: "BoardTitle",
                table: "BoardConfigurations");

            migrationBuilder.DropColumn(
                name: "BoardType",
                table: "BoardConfigurations");

            migrationBuilder.DropColumn(
                name: "ContactLabel",
                table: "BoardConfigurations");

            migrationBuilder.DropColumn(
                name: "SubtitleLabel",
                table: "BoardConfigurations");
        }
    }
}
