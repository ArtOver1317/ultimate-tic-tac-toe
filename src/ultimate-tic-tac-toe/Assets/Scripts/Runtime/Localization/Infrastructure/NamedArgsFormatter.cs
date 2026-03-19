using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Runtime.Localization
{
    public sealed class NamedArgsFormatter : ITextFormatter
    {
        public string Format(string template, LocaleId locale, IReadOnlyDictionary<string, object> args)
        {
            if (template == null)
                return string.Empty;

            if (args == null || args.Count == 0)
                return template;

            var culture = GetCulture(locale);

            var sb = new StringBuilder(template.Length + 16);
            
            for (var i = 0; i < template.Length; i++)
            {
                var ch = template[i];
                
                if (ch != '{')
                {
                    sb.Append(ch);
                    continue;
                }

                var close = template.IndexOf('}', i + 1);
                
                if (close < 0)
                {
                    sb.Append(ch);
                    continue;
                }

                var name = template.Substring(i + 1, close - i - 1);
                
                if (string.IsNullOrWhiteSpace(name))
                {
                    sb.Append('{');
                    i = close;
                    continue;
                }

                name = name.Trim();
                
                if (args.TryGetValue(name, out var value))
                    sb.Append(FormatValue(value, culture));
                else
                {
                    // Keep original placeholder if missing arg.
                    sb.Append(template, i, close - i + 1);
                }

                i = close;
            }

            return sb.ToString();
        }

        private static CultureInfo GetCulture(LocaleId locale)
        {
            try
            {
                return CultureInfo.GetCultureInfo(locale.Code);
            }
            catch
            {
                if (locale.TryGetLanguageOnly(out var languageOnly))
                {
                    try
                    {
                        return CultureInfo.GetCultureInfo(languageOnly.Code);
                    }
                    catch
                    {
                        return CultureInfo.InvariantCulture;
                    }
                }

                return CultureInfo.InvariantCulture;
            }
        }

        private static string FormatValue(object value, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            if (value is string s)
                return s;

            if (value is IFormattable f)
                return f.ToString(null, culture);

            return value.ToString();
        }
    }
}