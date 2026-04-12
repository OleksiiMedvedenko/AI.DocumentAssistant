using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchUploadAndFolderChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAtUtc",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstOpenedAtUtc",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNew",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingProfile",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "QuickSummary",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "ChatSessions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "ChatSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_FolderId",
                table: "ChatSessions",
                column: "FolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_FolderId",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "AnalyzedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FirstOpenedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsNew",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingProfile",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "QuickSummary",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "ChatSessions");

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "ChatSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
