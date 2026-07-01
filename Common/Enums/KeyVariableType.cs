namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Supported variable types for SmartFormat preview dictionary construction.
    /// Array types accept comma-separated sample values.
    /// </summary>
    public enum KeyVariableType
    {
        String,
        Int,
        Float,
        Bool,
        StringArray,
        IntArray,
        FloatArray,
        BoolArray,

        /// <summary>Passes the matching int value of the selected enum entry.</summary>
        EnumInt,

        /// <summary>Passes the matching string value of the selected enum entry.</summary>
        EnumString
    }
}