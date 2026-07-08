using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class LocProjectMember
    {
        private static readonly Guid _OfflineId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        public static readonly LocProjectMember OfflineMember = new()
        {
            UserId   = _OfflineId,
            Username = "offline.user",
            IsAdmin  = true
        };

        public Guid            UserId                    { get; set; }
        public string          Username                  { get; set; } = string.Empty;
        public HashSet<string> ReviewLanguagePermissions { get; set; } = new();
        public bool            IsAdmin                   { get; set; }
        public bool            IsBanned                  { get; set; }
        public string          HashedAccessToken         { get; set; } = string.Empty;

        /// <summary>
        /// True while the member still carries a one-time token issued by an admin (on creation or a
        /// token reset). First sign-in rotates it to a member-chosen token and clears this flag.
        /// </summary>
        public bool            MustResetAccessToken      { get; set; }

        public bool IsOfflineUser => UserId == _OfflineId;
    }
}