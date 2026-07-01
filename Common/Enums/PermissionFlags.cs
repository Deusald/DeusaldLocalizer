using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>Permission flags — combinable per project member.</summary>
    [Flags]
    public enum PermissionFlags
    {
        None    = 0,
        Vote    = 1 << 0, // Can vote on suggestions
        Suggest = 1 << 1, // Can add suggestions
        Review  = 1 << 2, // Can accept/reject suggestions, set approved translation
        Admin   = 1 << 3, // Full project control

        All = Vote | Suggest | Review | Admin
    }
}