using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    public partial class RedesignDocumentOrganization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FolderClassificationReason",
                table: "Documents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationMode",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "SmartOrganizeRequested",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
UPDATE [Documents]
SET
    [SmartOrganizeRequested] = 0,
    [OrganizationMode] =
        CASE
            WHEN [FolderId] IS NOT NULL THEN 0
            ELSE 1
        END
");

            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE Name = N'IsSmartOrganizationEnabled'
      AND Object_ID = Object_ID(N'[Documents]')
)
BEGIN
    ALTER TABLE [Documents] DROP COLUMN [IsSmartOrganizationEnabled];
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSmartOrganizationEnabled",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
UPDATE [Documents]
SET [IsSmartOrganizationEnabled] =
    CASE
        WHEN [OrganizationMode] = 1 THEN 0
        ELSE 1
    END
");

            migrationBuilder.DropColumn(
                name: "FolderClassificationReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OrganizationMode",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SmartOrganizeRequested",
                table: "Documents");
        }
    }
}