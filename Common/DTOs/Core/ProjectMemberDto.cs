using System;

namespace DeusaldLocalizerCommon
{
    public class ProjectMemberDto
    {
        public Guid            Id          { get; set; } = Guid.NewGuid();
        public Guid            UserId      { get; set; }
        public string          DisplayName { get; set; } = string.Empty; // Denormalised for offline display
        public PermissionFlags Permissions { get; set; } = PermissionFlags.Vote;
        public DateTime        JoinedAt    { get; set; } = DateTime.UtcNow;
    }
}