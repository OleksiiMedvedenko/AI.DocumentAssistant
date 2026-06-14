using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class OrganizationMemberRoleConfiguration : IEntityTypeConfiguration<OrganizationMemberRole>
{
    public void Configure(EntityTypeBuilder<OrganizationMemberRole> builder)
    {
        builder.ToTable("OrganizationMemberRoles");
        builder.HasKey(x => new { x.OrganizationMemberId, x.RoleId });
        builder.HasOne(x => x.OrganizationMember)
            .WithMany(x => x.Roles)
            .HasForeignKey(x => x.OrganizationMemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
