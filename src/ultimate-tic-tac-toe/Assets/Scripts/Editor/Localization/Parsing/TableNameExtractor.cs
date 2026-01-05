using System;

namespace Editor.Localization.Parsing
{
    /// <summary>
    /// Extracts table name from localization keys following the convention: "TableName.KeyName".
    /// Examples: "MainMenu.Title" → "MainMenu", "UI.Button.OK" → "UI"
    /// </summary>
    public sealed class TableNameExtractor
    {
        private readonly string _defaultTableName;

        /// <summary>
        /// Creates a new TableNameExtractor.
        /// </summary>
        /// <param name="defaultTableName">Default table name when key has no prefix (default: "UI").</param>
        public TableNameExtractor(string defaultTableName = "UI")
        {
            if (string.IsNullOrWhiteSpace(defaultTableName))
                throw new ArgumentException("Default table name cannot be null or empty.", nameof(defaultTableName));

            _defaultTableName = defaultTableName;
        }

        /// <summary>
        /// Extracts table name from a localization key.
        /// </summary>
        /// <param name="key">The localization key (e.g., "MainMenu.Title").</param>
        /// <returns>Table name extracted from key, or default table name if key has no prefix.</returns>
        /// <example>
        /// "MainMenu.Title" → "MainMenu"
        /// "UI.Button.OK" → "UI" (first part only)
        /// "Title" → "UI" (default, no dot)
        /// ".Title" → "UI" (default, starts with dot)
        /// </example>
        public string Extract(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return _defaultTableName;

            var dotIndex = key.IndexOf('.');

            if (dotIndex <= 0) // No dot or starts with dot
                return _defaultTableName;

            var tableName = key[..dotIndex].Trim();

            return string.IsNullOrWhiteSpace(tableName) 
                ? _defaultTableName 
                : tableName;
        }
    }
}
