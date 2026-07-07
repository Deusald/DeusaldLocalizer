using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Filter options for <see cref="LocalizationExportService"/>.
    /// A key is exported only when it passes both the flag and the tag test:
    ///   - flags: (IncludeFlags empty OR key has any included flag) AND key has none of ExcludeFlags
    ///   - tags:  (IncludeTags  empty OR key has any included tag)  AND key has none of ExcludeTags
    /// Only languages listed in <see cref="Languages"/> are written as columns
    /// (empty = every project language). Ordering is always source-first, rest alphabetical.
    /// </summary>
    public class LocExportOptions
    {
        public HashSet<FlagType> IncludeFlags { get; } = new();
        public HashSet<FlagType> ExcludeFlags { get; } = new();
        public HashSet<string>   IncludeTags  { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string>   ExcludeTags  { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>      Languages    { get; } = new();
    }
}
