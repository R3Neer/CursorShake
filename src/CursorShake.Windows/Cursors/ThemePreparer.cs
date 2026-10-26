using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors.Formats;
using Tomlyn;
using Tomlyn.Model;

namespace CursorShake.Windows.Cursors;

public sealed class PreparedTheme : IDisposable
{
    public PreparedTheme(string id,IReadOnlyDictionary<CursorRole,CursorAsset> roles)
    { Id=id;Roles=roles;EstimatedBytes=roles.Values.Distinct().Sum(x=>x.EstimatedBytes); }
    public string Id { get; }
    public IReadOnlyDictionary<CursorRole,CursorAsset> Roles { get; }
    public long EstimatedBytes { get; }
    public void Dispose(){foreach(var asset in Roles.Values.Distinct())asset.Dispose();}
}

public sealed class ThemePreparer(string bundledRoot)
{
    private readonly CursorAssetLoader loader=new();
    private static readonly Dictionary<CursorRole,(string File,double X,double Y)> WindowsSvg=new()
    {
        [CursorRole.Arrow]=("arrow.svg",1,1),[CursorRole.Text]=("ibeam.svg",16,16),
        [CursorRole.Cross]=("cross.svg",16,16),[CursorRole.UpArrow]=("up.svg",6,1),
        [CursorRole.Handwriting]=("pen.svg",1,1),[CursorRole.SizeNwSe]=("nwse.svg",8,8),
        [CursorRole.SizeNeSw]=("nesw.svg",9,8),[CursorRole.SizeWe]=("ew.svg",12,6),
        [CursorRole.SizeNs]=("ns.svg",5,12),[CursorRole.SizeAll]=("move.svg",12,12),
        [CursorRole.No]=("unavail.svg",8,8),[CursorRole.Hand]=("link.svg",8,1),
        [CursorRole.Help]=("helpsel.svg",1,1),[CursorRole.Pin]=("pin.svg",8,1),[CursorRole.Person]=("person.svg",8,1)
    };
    public IReadOnlyList<string> AvailableThemes(AppConfiguration configuration)=>new[]{"windows","bibata-ice","nordzy-white"}.Concat(configuration.PersonalThemes.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public PreparedTheme Prepare(AppConfiguration configuration,string workspaceRoot,CancellationToken cancellation=default)
    {
        var assets=new Dictionary<string,CursorAsset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var windows=ReadBundledDefinition("windows");
            string canonical=configuration.Theme.ToLowerInvariant();
            bool builtin=canonical is "windows" or "bibata-ice" or "nordzy-white";
            var definition=builtin?ReadBundledDefinition(canonical):configuration.PersonalThemes.TryGetValue(configuration.Theme,out var custom)?custom:throw new ConfigurationException("UnknownTheme","Unknown theme: "+configuration.Theme);
            var roles=new Dictionary<CursorRole,CursorAsset>();
            foreach(var role in CursorRoles.All)
            {
                cancellation.ThrowIfCancellationRequested();
                string? file=definition.Roles.TryGetValue(role,out var specific)?specific:definition.DefaultCursor;
                CursorAsset asset;
                if(file!=null)
                {
                    var folder=builtin?Path.Combine(bundledRoot,canonical):Path.Combine(workspaceRoot,"themes",configuration.Theme);
                    string path=ContainedPath(folder,file);
                    asset=Get(path,()=>loader.Load(WorkspaceResources.ReadContained(folder,file,CursorAssetLoader.MaximumFileBytes),Path.GetExtension(file)));
                }
                else if(windows.Roles.TryGetValue(role,out var windowsFile))
                {
                    string path=ContainedPath(Path.Combine(bundledRoot,"windows"),windowsFile);
                    asset=Get(path,()=>loader.Load(WorkspaceResources.ReadContained(Path.Combine(bundledRoot,"windows"),windowsFile,CursorAssetLoader.MaximumFileBytes),Path.GetExtension(windowsFile)));
                }
                else
                {
                    if(!WindowsSvg.TryGetValue(role,out var metadata))throw new ConfigurationException("MissingWindowsRole","No standard Windows reference for "+role);
                    string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Cursors",metadata.File);
                    asset=Get("system:"+path,()=>LoadSystemSvg(path,metadata.X,metadata.Y));
                }
                roles[role]=asset;
                if(assets.Values.Sum(x=>x.EstimatedBytes)>CursorAssetLoader.MaximumSourceBytes)throw new CursorAssetException(AssetErrorCode.LimitExceeded,"Prepared theme exceeds64MiB source/model budget.");
            }
            return new(configuration.Theme,roles);
        }
        catch{foreach(var asset in assets.Values.Distinct())asset.Dispose();throw;}
        CursorAsset Get(string key,Func<CursorAsset> create)
        {if(!assets.TryGetValue(key,out var asset)){asset=create();assets.Add(key,asset);}return asset;}
    }
    private PersonalThemeDefinition ReadBundledDefinition(string id)
    {
        string path=ContainedPath(bundledRoot,id+"/theme.toml");
        var bytes=WorkspaceResources.ReadContained(bundledRoot,id+"/theme.toml",65536);
        string text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
        var syntax=Toml.Parse(text);if(syntax.HasErrors)throw new ConfigurationException("InvalidTheme",syntax.Diagnostics.ToString(),path);
        var model=Toml.ToModel(text);var roles=new Dictionary<CursorRole,string>();
        if(model.TryGetValue("roles",out var values)&&values is TomlTable table)
            foreach(var (key,value) in table)
            {
                if(!Enum.TryParse<CursorRole>(key,true,out var role)||!Enum.IsDefined(role)||value is not string file)throw new ConfigurationException("InvalidThemeRole","Invalid role association: "+key,path);
                roles.Add(role,file);
            }
        return new(){DefaultCursor=model.TryGetValue("default",out var d)?d as string:null,Roles=roles};
    }
    private CursorAsset LoadSystemSvg(string path,double x,double y)
    {
        // System SVGs are read locally and wrapped only in memory; never copied to the distribution.
        byte[] original=WorkspaceResources.ReadSystemReference(path,CursorAssetLoader.MaximumSvgBytes);
        string fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
        if(!WindowsVectorReferences.Hashes.TryGetValue(Path.GetFileName(path),out var expected)||fingerprint!=expected)
            throw new ConfigurationException("UnverifiedWindowsVector","This Windows cursor source differs from the verified vector resource; no theme was applied.",path);
        using var xml=XmlReader.Create(new MemoryStream(original),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=CursorAssetLoader.MaximumSvgBytes });
        var document=XDocument.Load(xml);var root=document.Root??throw new InvalidDataException("Empty system SVG.");
        // These local Windows resources use a bottom-up canvas. Normalize it once before rasterization.
        var group=new XElement(root.Name.Namespace+"g",new XAttribute("transform","translate(0 32) scale(1 -1)"),root.Nodes().ToArray());
        root.RemoveNodes();root.Add(group);
        byte[] source=Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            using(var manifest=new StreamWriter(zip.CreateEntry("cursor.toml").Open(),new UTF8Encoding(false)))
                manifest.Write(FormattableString.Invariant($"version=1\nimage=\"cursor.svg\"\nhotspot=[{x},{y}]\n"));
            using var svg=zip.CreateEntry("cursor.svg").Open();svg.Write(source);
        }
        return loader.Load(output.ToArray(),".vcur");
    }
    public static string ContainedPath(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Split(['/', '\\']).Any(x=>x is "" or "." or ".."))throw new ConfigurationException("UnsafeResourcePath","Resource path must remain inside its theme folder: "+relative);
        string fullRoot=Path.GetFullPath(root),full=Path.GetFullPath(Path.Combine(fullRoot,relative));
        if(!full.StartsWith(fullRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ConfigurationException("UnsafeResourcePath","Resource escapes theme folder.");
        for(string? current=full;current!=null;current=Path.GetDirectoryName(current))
        {
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ConfigurationException("ResourceLink","Resource links are not supported.",current);

        }
        return full;
    }
}
