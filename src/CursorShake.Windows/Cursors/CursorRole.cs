namespace CursorShake.Windows.Cursors;

public enum CursorRole : uint
{
    Arrow = 32512, Text = 32513, Wait = 32514, Cross = 32515, UpArrow = 32516,
    Handwriting = 32631, SizeNwSe = 32642, SizeNeSw = 32643, SizeWe = 32644,
    SizeNs = 32645, SizeAll = 32646, No = 32648, Hand = 32649, Working = 32650,
    Help = 32651, Pin = 32671, Person = 32672
}

public static class CursorRoles
{
    public static IReadOnlyList<CursorRole> All { get; } = Enum.GetValues<CursorRole>();
    public static string RegistryName(this CursorRole role) => role switch
    {
        CursorRole.Arrow => "Arrow", CursorRole.Text => "IBeam", CursorRole.Wait => "Wait",
        CursorRole.Cross => "Crosshair", CursorRole.UpArrow => "UpArrow", CursorRole.Handwriting => "NWPen",
        CursorRole.SizeNwSe => "SizeNWSE", CursorRole.SizeNeSw => "SizeNESW", CursorRole.SizeWe => "SizeWE",
        CursorRole.SizeNs => "SizeNS", CursorRole.SizeAll => "SizeAll", CursorRole.No => "No",
        CursorRole.Hand => "Hand", CursorRole.Working => "AppStarting", CursorRole.Help => "Help",
        CursorRole.Pin => "Pin", CursorRole.Person => "Person", _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
