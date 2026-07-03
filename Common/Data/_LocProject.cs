using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class LocProject
    {
        public LocProjectMetadata       Metadata          { get; set; } = new();
        public List<LocProjectMember>   ProjectMembers    { get; set; } = new();
        public List<LocCategory>        Categories        { get; set; } = new();
        public List<LocEnum>            Enums             { get; set; } = new();
        public List<LocEntryChange>     UncommitedChanges { get; set; } = new();
        public List<LocLocalizationKey> Keys              { get; set; } = new();
    }
}