using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId",
                table: "Documents");

            migrationBuilder.AddColumn<decimal>(
                name: "FolderClassificationConfidence",
                table: "Documents",
                type: "decimal(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderClassificationStatus",
                table: "Documents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasFolderAutoAssigned",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DocumentFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NamePl = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameUa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFolders_DocumentFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFolders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FolderId",
                table: "Documents",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_FolderId_UploadedAtUtc",
                table: "Documents",
                columns: new[] { "UserId", "FolderId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_ParentFolderId",
                table: "DocumentFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_UserId_ParentFolderId_Key",
                table: "DocumentFolders",
                columns: new[] { "UserId", "ParentFolderId", "Key" },
                unique: true,
                filter: "[ParentFolderId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DocumentFolders_FolderId",
                table: "Documents",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DocumentFolders_FolderId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentFolders");

            migrationBuilder.DropIndex(
                name: "IX_Documents_FolderId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId_FolderId_UploadedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderClassificationConfidence",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderClassificationStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "WasFolderAutoAssigned",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId",
                table: "Documents",
                column: "UserId");
        }
    }
}
