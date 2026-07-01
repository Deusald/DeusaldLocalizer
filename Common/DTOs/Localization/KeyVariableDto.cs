using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Declares a SmartFormat variable for a key.
    /// SampleValue is used in the live preview so translators see realistic output.
    /// </summary>
    public class KeyVariableDto
    {
        public Guid            Id    { get; set; } = Guid.NewGuid();
        public Guid            KeyId { get; set; }
        public string          Name  { get; set; } = string.Empty;
        public KeyVariableType Type  { get; set; } = KeyVariableType.String;

        /// <summary>
        /// Required when Type is EnumInt or EnumString.
        /// References a LocEnumDto.Id on the project.
        /// </summary>
        public Guid? EnumId { get; set; }

        /// <summary>
        /// The admin-set default preview value, serialized as a string:
        ///   - Scalar types: plain string representation (e.g. "42", "true", "Alice").
        ///   - Array types:  comma-separated values (e.g. "red,green,blue").
        ///   - Enum types:   the IntValue as a string (e.g. "1").
        /// Used to pre-populate previews; overridden locally per-session by users
        /// without saving to the DTO until an admin explicitly commits it.
        /// </summary>
        public string DefaultPreviewValue { get; set; } = string.Empty;
    }
}