namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Supported variable types for SmartFormat preview dictionary construction.
    /// Array types accept comma-separated sample values.
    /// </summary>
    public enum KeyVariableType
    {
        String      = 0,
        Int         = 1,
        Float       = 2,
        Bool        = 3,
        StringArray = 4,
        IntArray    = 5,
        FloatArray  = 6,
        BoolArray   = 7,

        /// <summary>Passes the matching int value of the selected enum entry.</summary>
        EnumInt = 8,

        /// <summary>Passes the matching string value of the selected enum entry.</summary>
        EnumString = 9
    }
}