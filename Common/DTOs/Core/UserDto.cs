using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Represents a user. In offline mode a single OfflineUser is always present.
    /// In online mode this comes from the API's identity store.
    /// </summary>
    public class UserDto
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public string DisplayName { get; set; } = string.Empty;
        public string Email       { get; set; } = string.Empty;
        public bool   IsOffline   { get; set; } = false;

        public static UserDto CreateOfflineUser() => new()
        {
            Id          = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            DisplayName = "Offline User",
            Email       = "offline@local",
            IsOffline   = true,
        };
    }
}