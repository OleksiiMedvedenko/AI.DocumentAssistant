using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid OwnerUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowPrivateDocuments { get; set; } = true;
    public bool AllowPublicShareLinks { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public User OwnerUser { get; set; } = default!;
    public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public ICollection<OrganizationInvitation> Invitations { get; set; } = new List<OrganizationInvitation>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<DocumentFolder> Folders { get; set; } = new List<DocumentFolder>();
    public ICollection<AccessGrant> AccessGrants { get; set; } = new List<AccessGrant>();
}
