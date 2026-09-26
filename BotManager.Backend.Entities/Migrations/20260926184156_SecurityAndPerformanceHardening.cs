using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotManager.Backend.Entities.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAndPerformanceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommandUsageLogs_CommandId",
                table: "CommandUsageLogs");

            migrationBuilder.DropIndex(
                name: "IX_BotCommands_BotId",
                table: "BotCommands");

            migrationBuilder.AddColumn<decimal>(
                name: "RoleId",
                table: "Teams",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "SystemLogs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<int>(
                name: "BotId",
                table: "SystemLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_BotId_Timestamp",
                table: "SystemLogs",
                columns: new[] { "BotId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_Timestamp",
                table: "SystemLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditLogs_Timestamp",
                table: "LoginAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsageLogs_CommandId_ExecutedAt",
                table: "CommandUsageLogs",
                columns: new[] { "CommandId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsageLogs_ExecutedAt",
                table: "CommandUsageLogs",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BotCommands_BotId_CommandName_SubCommandName",
                table: "BotCommands",
                columns: new[] { "BotId", "CommandName", "SubCommandName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropIndex(
                name: "IX_SystemLogs_BotId_Timestamp",
                table: "SystemLogs");

            migrationBuilder.DropIndex(
                name: "IX_SystemLogs_Timestamp",
                table: "SystemLogs");

            migrationBuilder.DropIndex(
                name: "IX_LoginAuditLogs_Timestamp",
                table: "LoginAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_CommandUsageLogs_CommandId_ExecutedAt",
                table: "CommandUsageLogs");

            migrationBuilder.DropIndex(
                name: "IX_CommandUsageLogs_ExecutedAt",
                table: "CommandUsageLogs");

            migrationBuilder.DropIndex(
                name: "IX_BotCommands_BotId_CommandName_SubCommandName",
                table: "BotCommands");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "BotId",
                table: "SystemLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "SystemLogs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsageLogs_CommandId",
                table: "CommandUsageLogs",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_BotCommands_BotId",
                table: "BotCommands",
                column: "BotId");
        }
    }
}
