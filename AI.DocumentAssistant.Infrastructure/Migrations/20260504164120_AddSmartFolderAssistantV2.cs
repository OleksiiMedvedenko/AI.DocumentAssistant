using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFolderAssistantV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiActionTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OutputFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiActionTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiActionTemplates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentFolderSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExistingFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProposedKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProposedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProposedNamePl = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProposedNameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProposedNameUa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProposedParentFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolderSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFolderSuggestions_DocumentFolders_ExistingFolderId",
                        column: x => x.ExistingFolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFolderSuggestions_DocumentFolders_ProposedParentFolderId",
                        column: x => x.ProposedParentFolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFolderSuggestions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentFolderSuggestions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserFolderRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    CreatedFromCorrection = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFolderRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFolderRules_DocumentFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFolderRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiActionTemplates_UserId_DocumentType_Name",
                table: "AiActionTemplates",
                columns: new[] { "UserId", "DocumentType", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderSuggestions_DocumentId_Rank",
                table: "DocumentFolderSuggestions",
                columns: new[] { "DocumentId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderSuggestions_ExistingFolderId",
                table: "DocumentFolderSuggestions",
                column: "ExistingFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderSuggestions_ProposedParentFolderId",
                table: "DocumentFolderSuggestions",
                column: "ProposedParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderSuggestions_UserId_DocumentId_Status",
                table: "DocumentFolderSuggestions",
                columns: new[] { "UserId", "DocumentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFolderRules_FolderId",
                table: "UserFolderRules",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFolderRules_UserId_FolderId_Pattern",
                table: "UserFolderRules",
                columns: new[] { "UserId", "FolderId", "Pattern" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiActionTemplates");

            migrationBuilder.DropTable(
                name: "DocumentFolderSuggestions");

            migrationBuilder.DropTable(
                name: "UserFolderRules");
        }
    }
}
