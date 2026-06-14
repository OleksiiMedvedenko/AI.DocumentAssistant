using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationsAndAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "OrganizationId", table: "Documents", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Visibility", table: "Documents", type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "Private");
            migrationBuilder.AddColumn<Guid>(name: "OrganizationId", table: "DocumentFolders", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Visibility", table: "DocumentFolders", type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "Private");
            migrationBuilder.AddColumn<bool>(name: "InheritPermissions", table: "DocumentFolders", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<Guid>(name: "OrganizationId", table: "AiActionTemplates", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Visibility", table: "AiActionTemplates", type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "Private");

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowPrivateDocuments = table.Column<bool>(type: "bit", nullable: false),
                    AllowPublicShareLinks = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.ForeignKey("FK_Organizations_Users_OwnerUserId", x => x.OwnerUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Permissions", x => x.Id));

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SuspendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey("FK_OrganizationMembers_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_OrganizationMembers_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitations", x => x.Id);
                    table.ForeignKey("FK_OrganizationInvitations_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_OrganizationInvitations_Users_AcceptedByUserId", x => x.AcceptedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_OrganizationInvitations_Users_InvitedByUserId", x => x.InvitedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey("FK_Roles_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey("FK_Teams_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey("FK_AuditLogs_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_AuditLogs_Users_ActorUserId", x => x.ActorUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey("FK_RolePermissions_Permissions_PermissionId", x => x.PermissionId, "Permissions", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_RolePermissions_Roles_RoleId", x => x.RoleId, "Roles", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberRoles",
                columns: table => new
                {
                    OrganizationMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberRoles", x => new { x.OrganizationMemberId, x.RoleId });
                    table.ForeignKey("FK_OrganizationMemberRoles_OrganizationMembers_OrganizationMemberId", x => x.OrganizationMemberId, "OrganizationMembers", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_OrganizationMemberRoles_Roles_RoleId", x => x.RoleId, "Roles", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => new { x.TeamId, x.OrganizationMemberId });
                    table.ForeignKey("FK_TeamMembers_OrganizationMembers_OrganizationMemberId", x => x.OrganizationMemberId, "OrganizationMembers", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_TeamMembers_Teams_TeamId", x => x.TeamId, "Teams", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => x.Id);
                    table.ForeignKey("FK_AccessGrants_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_AccessGrants_Roles_RoleId", x => x.RoleId, "Roles", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_AccessGrants_Teams_TeamId", x => x.TeamId, "Teams", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_AccessGrants_Users_GrantedByUserId", x => x.GrantedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_AccessGrants_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_Organizations_OwnerUserId", "Organizations", "OwnerUserId");
            migrationBuilder.CreateIndex("IX_Organizations_Slug", "Organizations", "Slug", unique: true);
            migrationBuilder.CreateIndex("IX_Permissions_Key", "Permissions", "Key", unique: true);
            migrationBuilder.CreateIndex("IX_OrganizationMembers_OrganizationId_UserId", "OrganizationMembers", new[] { "OrganizationId", "UserId" }, unique: true);
            migrationBuilder.CreateIndex("IX_OrganizationMembers_UserId_Status", "OrganizationMembers", new[] { "UserId", "Status" });
            migrationBuilder.CreateIndex("IX_OrganizationInvitations_AcceptedByUserId", "OrganizationInvitations", "AcceptedByUserId");
            migrationBuilder.CreateIndex("IX_OrganizationInvitations_Code", "OrganizationInvitations", "Code", unique: true);
            migrationBuilder.CreateIndex("IX_OrganizationInvitations_InvitedByUserId", "OrganizationInvitations", "InvitedByUserId");
            migrationBuilder.CreateIndex("IX_OrganizationInvitations_OrganizationId_Email_Status", "OrganizationInvitations", new[] { "OrganizationId", "Email", "Status" });
            migrationBuilder.CreateIndex("IX_OrganizationInvitations_TokenHash", "OrganizationInvitations", "TokenHash", unique: true);
            migrationBuilder.CreateIndex("IX_Roles_OrganizationId_NormalizedName", "Roles", new[] { "OrganizationId", "NormalizedName" }, unique: true, filter: "[OrganizationId] IS NOT NULL");
            migrationBuilder.CreateIndex("IX_RolePermissions_PermissionId", "RolePermissions", "PermissionId");
            migrationBuilder.CreateIndex("IX_OrganizationMemberRoles_RoleId", "OrganizationMemberRoles", "RoleId");
            migrationBuilder.CreateIndex("IX_Teams_OrganizationId_NormalizedName", "Teams", new[] { "OrganizationId", "NormalizedName" }, unique: true);
            migrationBuilder.CreateIndex("IX_TeamMembers_OrganizationMemberId", "TeamMembers", "OrganizationMemberId");
            migrationBuilder.CreateIndex("IX_AccessGrants_GrantedByUserId", "AccessGrants", "GrantedByUserId");
            migrationBuilder.CreateIndex("IX_AccessGrants_OrganizationId_ResourceType_ResourceId_PermissionKey", "AccessGrants", new[] { "OrganizationId", "ResourceType", "ResourceId", "PermissionKey" });
            migrationBuilder.CreateIndex("IX_AccessGrants_RoleId", "AccessGrants", "RoleId");
            migrationBuilder.CreateIndex("IX_AccessGrants_TeamId", "AccessGrants", "TeamId");
            migrationBuilder.CreateIndex("IX_AccessGrants_UserId", "AccessGrants", "UserId");
            migrationBuilder.CreateIndex("IX_AuditLogs_ActorUserId", "AuditLogs", "ActorUserId");
            migrationBuilder.CreateIndex("IX_AuditLogs_OrganizationId_OccurredAtUtc", "AuditLogs", new[] { "OrganizationId", "OccurredAtUtc" });
            migrationBuilder.CreateIndex("IX_Documents_OrganizationId_Visibility_UploadedAtUtc", "Documents", new[] { "OrganizationId", "Visibility", "UploadedAtUtc" });
            migrationBuilder.CreateIndex("IX_DocumentFolders_OrganizationId_ParentFolderId_Key", "DocumentFolders", new[] { "OrganizationId", "ParentFolderId", "Key" });
            migrationBuilder.CreateIndex("IX_AiActionTemplates_OrganizationId_Visibility_DocumentType_Name", "AiActionTemplates", new[] { "OrganizationId", "Visibility", "DocumentType", "Name" });

            migrationBuilder.AddForeignKey("FK_Documents_Organizations_OrganizationId", "Documents", "OrganizationId", "Organizations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_DocumentFolders_Organizations_OrganizationId", "DocumentFolders", "OrganizationId", "Organizations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_AiActionTemplates_Organizations_OrganizationId", "AiActionTemplates", "OrganizationId", "Organizations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_Documents_Organizations_OrganizationId", "Documents");
            migrationBuilder.DropForeignKey("FK_DocumentFolders_Organizations_OrganizationId", "DocumentFolders");
            migrationBuilder.DropForeignKey("FK_AiActionTemplates_Organizations_OrganizationId", "AiActionTemplates");
            migrationBuilder.DropTable("AccessGrants");
            migrationBuilder.DropTable("AuditLogs");
            migrationBuilder.DropTable("OrganizationInvitations");
            migrationBuilder.DropTable("OrganizationMemberRoles");
            migrationBuilder.DropTable("RolePermissions");
            migrationBuilder.DropTable("TeamMembers");
            migrationBuilder.DropTable("Permissions");
            migrationBuilder.DropTable("Roles");
            migrationBuilder.DropTable("Teams");
            migrationBuilder.DropTable("OrganizationMembers");
            migrationBuilder.DropTable("Organizations");
            migrationBuilder.DropIndex("IX_Documents_OrganizationId_Visibility_UploadedAtUtc", "Documents");
            migrationBuilder.DropIndex("IX_DocumentFolders_OrganizationId_ParentFolderId_Key", "DocumentFolders");
            migrationBuilder.DropIndex("IX_AiActionTemplates_OrganizationId_Visibility_DocumentType_Name", "AiActionTemplates");
            migrationBuilder.DropColumn("OrganizationId", "Documents");
            migrationBuilder.DropColumn("Visibility", "Documents");
            migrationBuilder.DropColumn("OrganizationId", "DocumentFolders");
            migrationBuilder.DropColumn("Visibility", "DocumentFolders");
            migrationBuilder.DropColumn("InheritPermissions", "DocumentFolders");
            migrationBuilder.DropColumn("OrganizationId", "AiActionTemplates");
            migrationBuilder.DropColumn("Visibility", "AiActionTemplates");
        }
    }
}
