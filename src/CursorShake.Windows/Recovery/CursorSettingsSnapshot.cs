using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
namespace CursorShake.Windows.Recovery;

internal readonly record struct CursorRegistryValue(string Name,RegistryValueKind Kind,object Value);

/// <summary>Exact value kinds and unexpanded contents; immutable even when a registry value is an array.</summary>
internal sealed class CursorSettingsSnapshot : IEquatable<CursorSettingsSnapshot>
{
    private readonly SortedDictionary<string,string> values;
    private readonly bool exists;
    private CursorSettingsSnapshot(bool exists,SortedDictionary<string,string> values){this.exists=exists;this.values=values;}
    internal static CursorSettingsSnapshot Capture()
    {
        var before=Read();var after=Read();
        if(!before.Equals(after))throw new IOException("Cursor registry settings changed during capture; baseline ownership was not adopted.");
        return before;
    }
    private static CursorSettingsSnapshot Read()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
        if(key is null)return FromValues(false,[]);
        var captured=new List<CursorRegistryValue>();
        foreach(string name in key.GetValueNames())
        {
            var kind=key.GetValueKind(name);
            object value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)??throw new IOException("Cursor registry value disappeared during capture: "+name);
            captured.Add(new(name,kind,value));
        }
        return FromValues(true,captured);
    }
    internal static CursorSettingsSnapshot FromValues(bool keyExists,IEnumerable<CursorRegistryValue> source)
    {
        var result=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var value in source)
        {
            if(!keyExists)throw new InvalidDataException("An absent registry key cannot contain values.");
            string data=value.Kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString when value.Value is string text=>JsonSerializer.Serialize(text),
                RegistryValueKind.MultiString when value.Value is string[] items&&items.All(s=>s is not null)=>JsonSerializer.Serialize(items),
                RegistryValueKind.Binary or RegistryValueKind.None when value.Value is byte[] bytes=>Convert.ToHexString(bytes),
                RegistryValueKind.DWord when value.Value is int number=>number.ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.QWord when value.Value is long number=>number.ToString(CultureInfo.InvariantCulture),
                _=>throw new InvalidDataException("Unsupported cursor registry value kind or content: "+value.Name+" ("+value.Kind+").")
            };
            if(!result.TryAdd(value.Name,((int)value.Kind).ToString(CultureInfo.InvariantCulture)+":"+data))throw new InvalidDataException("Duplicate cursor registry value: "+value.Name);
        }
        return new(keyExists,result);
    }
    public bool Equals(CursorSettingsSnapshot? other)=>other is not null&&exists==other.exists&&values.Count==other.values.Count&&values.All(pair=>other.values.TryGetValue(pair.Key,out var value)&&string.Equals(pair.Value,value,StringComparison.Ordinal));
    public override bool Equals(object? other)=>other is CursorSettingsSnapshot snapshot&&Equals(snapshot);
    public override int GetHashCode(){var hash=new HashCode();hash.Add(exists);foreach(var pair in values){hash.Add(pair.Key,StringComparer.OrdinalIgnoreCase);hash.Add(pair.Value,StringComparer.Ordinal);}return hash.ToHashCode();}
}
