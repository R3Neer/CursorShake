using System.Text.Json;
using System.Text.RegularExpressions;
namespace CursorShake.Windows.Configuration;

public static class ConfigurationEdits
{
    public static string SetTheme(string text, string theme)
    {
        if (!Regex.IsMatch(theme, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")) throw new ArgumentException("Invalid theme identity.", nameof(theme));
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool trailing = text.EndsWith('\n');
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (trailing) lines.RemoveAt(lines.Count - 1);
        int table = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*\[appearance\]\s*(?:#.*)?$"));
        string literal = JsonSerializer.Serialize(theme);
        if (table < 0)
        {
            var model = Tomlyn.Toml.ToModel(text);
            if (model.ContainsKey("appearance")) throw new ConfigurationException("Conflict", "The appearance table uses quoted or dotted syntax; edit its theme in the configuration file to preserve that syntax.");
            lines.Add(""); lines.Add("[appearance]"); lines.Add("theme = " + literal);
        }
        else
        {
            int end = table + 1; while (end < lines.Count && !Regex.IsMatch(lines[end], @"^\s*\[")) end++;
            int field = -1;
            for (int i = table + 1; i < end; i++) if (Regex.IsMatch(lines[i], "^\\s*(?:theme|\"theme\"|'theme')\\s*=")) { field = i; break; }
            if (field < 0) lines.Insert(table + 1, "theme = " + literal);
            else
            {
                var parsed = Regex.Match(lines[field], "^(?<prefix>\\s*(?:theme|\"theme\"|'theme')\\s*=\\s*)(?:\"(?:\\\\.|[^\"])*\"|'[^']*')(?<tail>.*)$");
                if (!parsed.Success) throw new ConfigurationException("Conflict", "Theme field cannot be edited without rewriting unrelated content.");
                lines[field] = parsed.Groups["prefix"].Value + literal + parsed.Groups["tail"].Value;
            }
        }
        return string.Join(newline, lines) + (trailing ? newline : "");
    }
}
