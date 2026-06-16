using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.TitleKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MessageKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.DismissedAtUtc, x.ReadAtUtc, x.CreatedAtUtc });
        builder.HasIndex(x => x.RelatedOrganizationId);
        builder.HasIndex(x => x.RelatedInvitationId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.RelatedOrganization)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.RelatedOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RelatedInvitation)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.RelatedInvitationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
