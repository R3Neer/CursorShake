using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CursorShake.Core.Growth;
using CursorShake.Core.Shake;
using CursorShake.Windows.Cursors;
using Tomlyn;
using Tomlyn.Model;
namespace CursorShake.Windows.Configuration;

public static class ConfigParser
{
    public const int MaximumTextBytes = 1024 * 1024;
    public static AppConfiguration Parse(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumTextBytes) throw new ConfigurationException("LimitExceeded", "Configuration exceeds 1 MiB.", sourcePath);
        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
        {
            string detail = syntax.Diagnostics.ToString();
            var location = Regex.Match(detail, @"\((\d+),(\d+)\)");
            throw new ConfigurationException("InvalidToml", detail, sourcePath, location.Success ? int.Parse(location.Groups[1].Value, CultureInfo.InvariantCulture) : null, location.Success ? int.Parse(location.Groups[2].Value, CultureInfo.InvariantCulture) : null);
        }
        var root = Toml.ToModel(text);
        ConfigurationException Invalid(string message, string? key = null)
        {
            int? line = null;
            if (key is not null)
            {
                var match = Regex.Match(text, @"(?m)^\s*" + Regex.Escape(key.Split('.').Last()) + @"\s*=");
                if (match.Success) line = text.AsSpan(0, match.Index).Count('\n') + 1;
            }
            return new ConfigurationException("InvalidConfiguration", message, sourcePath, line, line is null ? null : 1);
        }
        void Keys(TomlTable table, string path, params string[] allowed)
        { foreach (var key in table.Keys) if (!allowed.Contains(key, StringComparer.Ordinal)) throw Invalid("Unknown field: " + path + key, key); }
        TomlTable Table(TomlTable table, string key)
        { if (!table.TryGetValue(key, out var value)) return new TomlTable(); return value as TomlTable ?? throw Invalid("Expected table: " + key, key); }
        double Number(TomlTable table, string key, double fallback)
        {
            if (!table.TryGetValue(key, out var value)) return fallback;
            double number = value switch { long integer => integer, double fraction => fraction, _ => throw Invalid("Expected number: " + key, key) };
            if (!double.IsFinite(number)) throw Invalid("Expected finite number: " + key, key);
            return number;
        }
        long Integer(TomlTable table, string key, long fallback)
        { if (!table.TryGetValue(key, out var value)) return fallback; return value is long integer ? integer : throw Invalid("Expected integer: " + key, key); }
        string Text(TomlTable table, string key, string fallback)
        { if (!table.TryGetValue(key, out var value)) return fallback; return value is string s && !string.IsNullOrWhiteSpace(s) ? s : throw Invalid("Expected nonempty string: " + key, key); }
        TimeSpan Milliseconds(TomlTable table, string key, TimeSpan fallback)
        {
            double value = Number(table, key, fallback.TotalMilliseconds);
            if (value < 0.001 || value > 3600000) throw Invalid(key + " must be positive and at most one hour.", key);
            return TimeSpan.FromMilliseconds(value);
        }
        string RelativeCursor(TomlTable table, string key)
        {
            string value = Text(table, key, "");
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':') || value.Split('/', '\\').Any(s => s is "" or "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw Invalid("Cursor path must remain relative inside its personal theme: " + value, key);
            if (!new[] { ".cur", ".ani", ".vcur", ".vani" }.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase)) throw Invalid("Unsupported cursor extension: " + value, key);
            return value;
        }
        Keys(root, "", "version", "appearance", "gesture", "growth", "themes");
        if (Integer(root, "version", 1) != 1) throw new ConfigurationException("UnsupportedVersion", "Only configuration version 1 is supported.", sourcePath);
        var appearance = Table(root, "appearance"); Keys(appearance, "appearance.", "theme");
        var gesture = Table(root, "gesture"); Keys(gesture, "gesture.", "count", "maximum_interval_ms", "minimum_travel", "minimum_speed");
        var growth = Table(root, "growth"); Keys(growth, "growth.", "maximum_scale", "growth_duration_ms", "inactivity_delay_ms", "return_duration_ms", "maximum_cadence_multiplier");
        var defaults = new GestureProfile(); var dg = defaults.Growth;
        long count = Integer(gesture, "count", defaults.CountToActivate);
        if (count is < 1 or > 100) throw Invalid("count must be between 1 and 100.", "count");
        var profile = new GestureProfile
        {
            CountToActivate = (int)count,
            MaximumInterval = Milliseconds(gesture, "maximum_interval_ms", defaults.MaximumInterval),
            MinimumTravel = Number(gesture, "minimum_travel", defaults.MinimumTravel),
            MinimumSpeed = Number(gesture, "minimum_speed", defaults.MinimumSpeed),
            Growth = new GrowthProfile
            {
                MaximumScale = Number(growth, "maximum_scale", dg.MaximumScale),
                GrowthDuration = Milliseconds(growth, "growth_duration_ms", dg.GrowthDuration),
                InactivityDelay = Milliseconds(growth, "inactivity_delay_ms", dg.InactivityDelay),
                ReturnDuration = Milliseconds(growth, "return_duration_ms", dg.ReturnDuration),
                MaximumCadenceMultiplier = Number(growth, "maximum_cadence_multiplier", dg.MaximumCadenceMultiplier)
            }
        };
        try { profile.Validate(); } catch (ArgumentException e) { throw Invalid(e.Message, e.ParamName); }
        var themes = new Dictionary<string, PersonalThemeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Table(root, "themes"))
        {
            if (!Regex.IsMatch(item.Key, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")) throw Invalid("Theme id must contain 1–64 letters, digits, '_' or '-': " + item.Key);
            if (item.Value is not TomlTable definition) throw Invalid("Theme definition must be a table: " + item.Key);
            Keys(definition, "themes." + item.Key + ".", "default", "roles");
            var roles = new Dictionary<CursorRole, string>();
            foreach (var role in Table(definition, "roles"))
            {
                if (!Enum.GetNames<CursorRole>().Contains(role.Key, StringComparer.OrdinalIgnoreCase) || !Enum.TryParse<CursorRole>(role.Key, true, out var parsed)) throw Invalid("Unknown cursor role: " + role.Key, role.Key);
                if (!roles.TryAdd(parsed, RelativeCursor(Table(definition, "roles"), role.Key))) throw Invalid("Duplicate cursor role: " + role.Key, role.Key);
            }
            if (!themes.TryAdd(item.Key, new PersonalThemeDefinition { DefaultCursor = definition.ContainsKey("default") ? RelativeCursor(definition, "default") : null, Roles = new ReadOnlyDictionary<CursorRole, string>(roles) })) throw Invalid("Theme ids differ only by case: " + item.Key);
        }
        string selected = Text(appearance, "theme", "windows");
        if (!new[] { "windows", "bibata-ice", "nordzy-white" }.Contains(selected, StringComparer.OrdinalIgnoreCase) && !themes.ContainsKey(selected)) throw Invalid("Selected theme is not declared: " + selected, "theme");
        return new AppConfiguration { Theme = selected, Gesture = profile, PersonalThemes = new ReadOnlyDictionary<string, PersonalThemeDefinition>(themes) };
    }
}
