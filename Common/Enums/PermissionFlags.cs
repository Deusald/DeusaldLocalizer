using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>Permission flags — combinable per project member.</summary>
    [Flags]
    public enum PermissionFlags
    {
        None        = 0,
        Voter       = 1 << 0, // Can vote on suggestions
        Contributor = 1 << 1, // Can add suggestions
        Reviewer    = 1 << 2, // Can accept/reject suggestions, set approved translation
        Admin       = 1 << 3, // Full project control
    }
}