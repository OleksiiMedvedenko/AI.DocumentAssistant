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

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Email, x.Status });
        builder.HasIndex(x => new { x.InvitedUserId, x.Status, x.ExpiresAtUtc });
        builder.HasIndex(x => x.InvitedByUserId);
        builder.HasIndex(x => x.RevokedByUserId);

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.InvitedUser)
            .WithMany(x => x.ReceivedOrganizationInvitations)
            .HasForeignKey(x => x.InvitedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InvitedByUser)
            .WithMany(x => x.SentOrganizationInvitations)
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RevokedByUser)
            .WithMany(x => x.RevokedOrganizationInvitations)
            .HasForeignKey(x => x.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
