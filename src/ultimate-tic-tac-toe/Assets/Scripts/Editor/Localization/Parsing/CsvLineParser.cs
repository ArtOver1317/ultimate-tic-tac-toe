using System.Collections.Generic;
using System.Text;

namespace Editor.Localization.Parsing
{
    /// <summary>
    /// Parses CSV lines with proper handling of quoted fields and escape sequences.
    /// Supports RFC 4180 CSV format: fields can be quoted with double quotes,
    /// and double quotes within quoted fields are escaped as "".
    /// </summary>
    public sealed class CsvLineParser
    {
        /// <summary>
        /// Parses a CSV line into an array of field values.
        /// </summary>
        /// <param name="line">The CSV line to parse.</param>
        /// <returns>Array of field values extracted from the line.</returns>
        /// <example>
        /// Input:  "Key,\"Value with, comma\",\"Value with \"\"quotes\"\"\""
        /// Output: ["Key", "Value with, comma", "Value with \"quotes\""]
        /// </example>
        public string[] Parse(string line)
        {
            if (string.IsNullOrEmpty(line))
                return System.Array.Empty<string>();

            var result = new List<string>();
            var currentField = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    // Handle escaped quotes ("" within quoted field)
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // Skip next quote
                    }
                    else
                    {
                        // Toggle quote mode
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // Field separator (only outside quotes)
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                    currentField.Append(c);
            }

            // Add last field
            result.Add(currentField.ToString());
            
            return result.ToArray();
        }
    }
}
