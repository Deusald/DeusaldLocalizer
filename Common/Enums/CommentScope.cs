namespace DeusaldLocalizerCommon
{
    /// <summary>Which comment list a <see cref="LocCommentRef"/> targets inside a key.</summary>
    public enum CommentScope
    {
        Key,         // key.Comments
        Translation, // the key+language translation's Comments
        Suggestion   // a translation suggestion's Comments
    }
}
