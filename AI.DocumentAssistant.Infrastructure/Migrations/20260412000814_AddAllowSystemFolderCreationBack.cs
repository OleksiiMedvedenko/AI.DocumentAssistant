using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    public partial class AddAllowSystemFolderCreationBack : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSystemFolderCreation",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowSystemFolderCreation",
                table: "Documents");
        }
    }
}