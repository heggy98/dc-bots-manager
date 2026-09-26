using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BotManager.Backend.Entities.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ActorEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardGlobalConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DefaultBoardTitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    DefaultBoardDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultBoardDetailSubtitleLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultBoardDetailContactLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardGlobalConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bots",
                columns: table => new
                {
                    BotId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BotToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    AutoStart = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastStartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastStoppedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bots", x => x.BotId);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    FailReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsBruteforceBlock = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BotId = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Exception = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardConfigurations",
                columns: table => new
                {
                    BoardConfigurationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotId = table.Column<int>(type: "integer", nullable: false),
                    BoardType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    BoardChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    BoardMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    BoardTitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    BoardDescriptionTemplate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubtitleLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "BotCommands",
                columns: table => new
                {
                    CommandId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotId = table.Column<int>(type: "integer", nullable: false),
                    CommandName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubCommandName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MinimumPermissionLevel = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CommandParameters = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UserHint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuccessMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PermissionMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AdminOnlyMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InvalidArgumentsMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotCommands", x => x.CommandId);
                    table.ForeignKey(
                        name: "FK_BotCommands_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "BotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BotRunHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotId = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    StopReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorDetails = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotRunHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotRunHistories_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "BotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BotConfigurations",
                columns: table => new
                {
                    BotId = table.Column<int>(type: "integer", nullable: false),
                    ActiveBoardConfigurationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotConfigurations", x => x.BotId);
                    table.ForeignKey(
                        name: "FK_BotConfigurations_BoardConfigurations_ActiveBoardConfigurat~",
                        column: x => x.ActiveBoardConfigurationId,
                        principalTable: "BoardConfigurations",
                        principalColumn: "BoardConfigurationId");
                    table.ForeignKey(
                        name: "FK_BotConfigurations_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "BotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoardConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LeaderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommanderContact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_Teams_BoardConfigurations_BoardConfigurationId",
                        column: x => x.BoardConfigurationId,
                        principalTable: "BoardConfigurations",
                        principalColumn: "BoardConfigurationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommandUsageLogs",
                columns: table => new
                {
                    UsageId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommandId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandUsageLogs", x => x.UsageId);
                    table.ForeignKey(
                        name: "FK_CommandUsageLogs_BotCommands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "BotCommands",
                        principalColumn: "CommandId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "BoardGlobalConfigs",
                columns: new[] { "Id", "DefaultBoardDescription", "DefaultBoardDetailContactLabel", "DefaultBoardDetailSubtitleLabel", "DefaultBoardTitle" },
                values: new object[] { 1, "Celkem registrovaných skupin: {count}", "Kontakt", "Podtitul", "📋 Seznam všech skupin" });

            migrationBuilder.InsertData(
                table: "SystemConfigs",
                columns: new[] { "Id", "Description", "Key", "Value" },
                values: new object[,]
                {
                    { 1, "Maximum failed login attempts before lockout", "Bruteforce.MaxAttempts", "5" },
                    { 2, "Minutes to lock out after too many failures", "Bruteforce.LockoutMinutes", "5" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_TargetType_TargetId",
                table: "AdminAuditLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Timestamp",
                table: "AdminAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_BoardConfigurations_BotId",
                table: "BoardConfigurations",
                column: "BotId");

            migrationBuilder.CreateIndex(
                name: "IX_BotCommands_BotId_CommandName_SubCommandName",
                table: "BotCommands",
                columns: new[] { "BotId", "CommandName", "SubCommandName" });

            migrationBuilder.CreateIndex(
                name: "IX_BotConfigurations_ActiveBoardConfigurationId",
                table: "BotConfigurations",
                column: "ActiveBoardConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BotRunHistories_BotId",
                table: "BotRunHistories",
                column: "BotId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsageLogs_CommandId_ExecutedAt",
                table: "CommandUsageLogs",
                columns: new[] { "CommandId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsageLogs_ExecutedAt",
                table: "CommandUsageLogs",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditLogs_Timestamp",
                table: "LoginAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Email_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "Email", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_BotId_Timestamp",
                table: "SystemLogs",
                columns: new[] { "BotId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_Timestamp",
                table: "SystemLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_BoardConfigurationId",
                table: "Teams",
                column: "BoardConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropTable(
                name: "BoardGlobalConfigs");

            migrationBuilder.DropTable(
                name: "BotConfigurations");

            migrationBuilder.DropTable(
                name: "BotRunHistories");

            migrationBuilder.DropTable(
                name: "CommandUsageLogs");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "LoginAuditLogs");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SystemConfigs");

            migrationBuilder.DropTable(
                name: "SystemLogs");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "BotCommands");

            migrationBuilder.DropTable(
                name: "BoardConfigurations");

            migrationBuilder.DropTable(
                name: "Bots");
        }
    }
}
