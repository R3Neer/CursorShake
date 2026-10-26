using System.Security.Principal;
using CursorShake.Windows.Lifecycle;
namespace CursorShake.Windows.Tests;

public sealed class StartupPrincipalTests
{
    [Fact] public void CurrentSidAndCanonicalAccountBothMatch()
    {
        var sid = WindowsIdentity.GetCurrent().User!; string account = sid.Translate(typeof(NTAccount)).Value;
        Assert.True(StartupRegistration.PrincipalMatchesSid(sid.Value, sid.Value));
        Assert.True(StartupRegistration.PrincipalMatchesSid(account, sid.Value));
        Assert.True(StartupRegistration.PrincipalMatchesSid(Environment.UserName, sid.Value));
    }
    [Fact] public void AnotherSidNeverMatchesCurrentUser()
    {
        string current = WindowsIdentity.GetCurrent().User!.Value;
        Assert.False(StartupRegistration.PrincipalMatchesSid("S-1-0-0", current));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("CursorShake-Unknown-63f520085e524c469063530243bd188b")]
    public void MissingOrUnresolvablePrincipalIsNotOwned(string? principal)
        => Assert.False(StartupRegistration.PrincipalMatchesSid(principal, WindowsIdentity.GetCurrent().User!.Value));
}
