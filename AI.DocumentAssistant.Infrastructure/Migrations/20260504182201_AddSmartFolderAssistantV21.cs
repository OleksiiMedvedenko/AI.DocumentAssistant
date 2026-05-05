using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFolderAssistantV21 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FinalScore",
                table: "DocumentFolderSuggestions",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RuleScore",
                table: "DocumentFolderSuggestions",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SemanticScore",
                table: "DocumentFolderSuggestions",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UserHistoryScore",
                table: "DocumentFolderSuggestions",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "DocumentIntelligenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessDomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Month = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIntelligenceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentIntelligenceSnapshots_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentIntelligenceSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FolderEmbeddingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderEmbeddingProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderEmbeddingProfiles_DocumentFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FolderEmbeddingProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIntelligenceSnapshots_DocumentId",
                table: "DocumentIntelligenceSnapshots",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIntelligenceSnapshots_UserId_DocumentType_BusinessDomain",
                table: "DocumentIntelligenceSnapshots",
                columns: new[] { "UserId", "DocumentType", "BusinessDomain" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIntelligenceSnapshots_UserId_Year_Month",
                table: "DocumentIntelligenceSnapshots",
                columns: new[] { "UserId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_FolderEmbeddingProfiles_FolderId",
                table: "FolderEmbeddingProfiles",
                column: "FolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FolderEmbeddingProfiles_UserId",
                table: "FolderEmbeddingProfiles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentIntelligenceSnapshots");

            migrationBuilder.DropTable(
                name: "FolderEmbeddingProfiles");

            migrationBuilder.DropColumn(
                name: "FinalScore",
                table: "DocumentFolderSuggestions");

            migrationBuilder.DropColumn(
                name: "RuleScore",
                table: "DocumentFolderSuggestions");

            migrationBuilder.DropColumn(
                name: "SemanticScore",
                table: "DocumentFolderSuggestions");

            migrationBuilder.DropColumn(
                name: "UserHistoryScore",
                table: "DocumentFolderSuggestions");
        }
    }
}
