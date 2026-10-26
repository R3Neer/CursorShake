using System.IO.MemoryMappedFiles;
namespace CursorShake.Windows.Recovery;

/// <summary>One writer; the recovery reader starts after parent death. Active aligned slots survive a partial write.</summary>
internal sealed class OwnershipLedger : IDisposable
{
    private const int Magic = 0x43535231, HeaderSize = 64, SlotSize = 80, RoleSize = 176;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly int _roles;
    private readonly object _sync = new();
    public OwnershipLedger(string name, int roles, bool create)
    {
        if (roles is < 1 or > 64) throw new InvalidDataException("Invalid ownership role count.");
        _roles = roles;
        _map = create ? MemoryMappedFile.CreateNew(name, HeaderSize + roles * RoleSize, MemoryMappedFileAccess.ReadWrite) : MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        _view = _map.CreateViewAccessor(0, HeaderSize + roles * RoleSize, MemoryMappedFileAccess.ReadWrite);
        if (create) { _view.Write(0, Magic); _view.Write(4, 1); _view.Write(16, roles); }
        else if (_view.ReadInt32(0) != Magic || _view.ReadInt32(4) != 1 || _view.ReadInt32(16) != roles) { Dispose(); throw new InvalidDataException("Invalid ownership ledger header."); }
    }
    public bool ExternalChange { get { Thread.MemoryBarrier(); return _view.ReadInt32(8) != 0; } }
    public bool Disarmed { get { Thread.MemoryBarrier(); return _view.ReadInt32(12) != 0; } }
    public void SetExternalChange() { _view.Write(8, 1); Thread.MemoryBarrier(); }
    public void Disarm() { Thread.MemoryBarrier(); _view.Write(12, 1); Thread.MemoryBarrier(); }
    public OwnershipState Read(int index)
    {
        if (index < 0 || index >= _roles) throw new ArgumentOutOfRangeException(nameof(index));
        int start = HeaderSize + index * RoleSize, active = _view.ReadInt32(start);
        if (active is < 0 or > 1) throw new InvalidDataException("Invalid ownership slot.");
        Thread.MemoryBarrier(); int offset = start + 16 + active * SlotSize;
        var previous = new byte[32]; var pending = new byte[32];
        _view.ReadArray(offset + 16, previous, 0, 32); _view.ReadArray(offset + 48, pending, 0, 32);
        int phase = _view.ReadInt32(offset + 8);
        if (phase is < 0 or > 3) throw new InvalidDataException("Invalid ownership phase.");
        return new(_view.ReadInt64(offset), (OwnershipPhase)phase, previous, pending);
    }
    public void Write(int index, OwnershipPhase phase, byte[] previous, byte[] pending)
    {
        if (previous.Length != 32 || pending.Length != 32) throw new ArgumentException("Ownership hashes must be SHA256.");
        lock (_sync)
        {
            var old = Read(index); int start = HeaderSize + index * RoleSize;
            int inactive = 1 - _view.ReadInt32(start), offset = start + 16 + inactive * SlotSize;
            _view.Write(offset, old.Generation + 1); _view.Write(offset + 8, (int)phase);
            _view.WriteArray(offset + 16, previous, 0, 32); _view.WriteArray(offset + 48, pending, 0, 32);
            Thread.MemoryBarrier(); _view.Write(start, inactive); Thread.MemoryBarrier();
        }
    }
    public void Dispose() { _view.Dispose(); _map.Dispose(); }
}
