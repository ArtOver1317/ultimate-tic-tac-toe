namespace Editor.Localization.Parsing
{
    /// <summary>
    /// Escapes special characters in strings for JSON format.
    /// Handles: backslash, quotes, newlines, carriage returns, tabs.
    /// </summary>
    public sealed class JsonStringEscaper
    {
        /// <summary>
        /// Escapes special characters in a string to make it JSON-safe.
        /// </summary>
        /// <param name="value">The string to escape.</param>
        /// <returns>JSON-escaped string.</returns>
        /// <example>
        /// Input:  "Line 1\nLine 2\tTab"
        /// Output: "Line 1\\nLine 2\\tTab"
        /// </example>
        public string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value
                .Replace("\\", "\\\\") // Backslash must be first
                .Replace("\"", "\\\"") // Double quotes
                .Replace("\n", "\\n") // Newline
                .Replace("\r", "\\r") // Carriage return
                .Replace("\t", "\\t"); // Tab
        }
    }
}
