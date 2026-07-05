using System.Collections.Generic;
using System.Linq;

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
        
        public int TotalNumberOfApprovedKeys()
        {
            return Keys.SelectMany(k => k.Translations)
                       .Count(t => Metadata.Languages.Contains(t.LanguageId) && t.Status == TranslationStatus.Approved);
        }

        public int GetNumberOfApprovedKeys(string langCode)
        {
            return Keys.SelectMany(k => k.Translations)
                       .Count(t => t.LanguageId == langCode && t.Status == TranslationStatus.Approved);
        }
    }
}