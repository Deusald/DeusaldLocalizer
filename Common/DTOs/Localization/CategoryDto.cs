using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Organises keys into a hierarchy. ParentCategoryId = null means root category.
    /// </summary>
    public class CategoryDto
    {
        public Guid   Id               { get; set; } = Guid.NewGuid();
        public Guid   ProjectId        { get; set; }
        public Guid?  ParentCategoryId { get; set; }
        public string Name             { get; set; } = string.Empty;
        public string Description      { get; set; } = string.Empty;
    }
}