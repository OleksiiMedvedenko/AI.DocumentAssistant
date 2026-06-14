using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.ToTable("OrganizationInvitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Email, x.Status });
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.InvitedByUser)
            .WithMany(x => x.SentOrganizationInvitations)
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcceptedByUser)
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
