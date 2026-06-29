using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class ProjectDto
    {
        public Guid     Id             { get; set; } = Guid.NewGuid();
        public string   Name           { get; set; } = string.Empty;
        public string   Slug           { get; set; } = string.Empty;
        public string   Description    { get; set; } = string.Empty;
        public string   MainLanguageId { get; set; } = string.Empty;
        public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt      { get; set; } = DateTime.UtcNow;
        public int      FormatVersion  { get; set; } = 1;

        public List<string>             Languages  { get; set; } = new();
        public List<ProjectMemberDto>   Members    { get; set; } = new();
        public List<CategoryDto>        Categories { get; set; } = new();
        public List<LocalizationKeyDto> Keys       { get; set; } = new();
    }
}