using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetNull_For_Folder_ChatSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_DocumentFolders_FolderId",
                table: "ChatSessions",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id");
        }
    }
}
