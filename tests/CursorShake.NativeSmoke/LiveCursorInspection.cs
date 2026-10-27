using System.Text.Json;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
namespace CursorShake.NativeSmoke;
internal static class LiveCursorInspection
{
    internal static int Run(string receiptDirectory,string output)
    {
        var manifest=BaselineStore.ReadManifest(receiptDirectory);
        var roles=manifest.Cursors.Select(role=>
        {
            string actual=Convert.ToHexString(CursorFingerprint.Current(role.Role));
            return new {Role=role.Role.ToString(),Baseline=role.Fingerprint,Actual=actual,Matches=actual==role.Fingerprint,Handle=CursorNative.LoadCursor(0,(nint)(uint)role.Role).ToInt64()};
        }).ToArray();
        File.WriteAllText(output,JsonSerializer.Serialize(new {ReadOnly=true,MutationAttempted=false,ObservedUtc=DateTimeOffset.UtcNow,manifest.ParentPid,manifest.Nonce,All17Match=roles.Length==17&&roles.All(role=>role.Matches),Roles=roles},new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
