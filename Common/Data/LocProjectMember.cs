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
        public string          HashedAccessToken         { get; set; } = string.Empty;

        public bool IsOfflineUser => UserId == _OfflineId;
    }
}