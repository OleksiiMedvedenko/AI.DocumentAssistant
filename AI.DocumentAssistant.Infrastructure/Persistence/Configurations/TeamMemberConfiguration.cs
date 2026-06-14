using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("TeamMembers");
        builder.HasKey(x => new { x.TeamId, x.OrganizationMemberId });
        builder.HasOne(x => x.Team)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.OrganizationMember)
            .WithMany(x => x.TeamMemberships)
            .HasForeignKey(x => x.OrganizationMemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
