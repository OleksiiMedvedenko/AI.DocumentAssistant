using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiActionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "AiActionTemplates",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "custom");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AiActionTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "AiActionTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AiActionTemplates",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SaveResult",
                table: "AiActionTemplates",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Prompt",
                table: "AiActionTemplates",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.CreateTable(
                name: "AiActionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutputFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResultText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultFilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResultFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ResultContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiActionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiActionRuns_AiActionTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "AiActionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiActionRuns_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AiActionRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiActionTemplates_UserId_FolderId",
                table: "AiActionTemplates",
                columns: new[] { "UserId", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiActionTemplates_FolderId",
                table: "AiActionTemplates",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiActionRuns_DocumentId",
                table: "AiActionRuns",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiActionRuns_TemplateId",
                table: "AiActionRuns",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AiActionRuns_UserId_DocumentId_CreatedAtUtc",
                table: "AiActionRuns",
                columns: new[] { "UserId", "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiActionRuns_UserId_Status",
                table: "AiActionRuns",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiActionRuns_UserId_TemplateId_CreatedAtUtc",
                table: "AiActionRuns",
                columns: new[] { "UserId", "TemplateId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiActionTemplates_DocumentFolders_FolderId",
                table: "AiActionTemplates",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiActionTemplates_DocumentFolders_FolderId",
                table: "AiActionTemplates");

            migrationBuilder.DropTable(
                name: "AiActionRuns");

            migrationBuilder.DropIndex(
                name: "IX_AiActionTemplates_UserId_FolderId",
                table: "AiActionTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AiActionTemplates_FolderId",
                table: "AiActionTemplates");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "AiActionTemplates");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AiActionTemplates");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "AiActionTemplates");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AiActionTemplates");

            migrationBuilder.DropColumn(
                name: "SaveResult",
                table: "AiActionTemplates");

            migrationBuilder.AlterColumn<string>(
                name: "Prompt",
                table: "AiActionTemplates",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
