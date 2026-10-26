namespace CursorShake.App.Dialogs;

public sealed record RecoveryReview(string Text, string Revision, bool CanKeepCurrent, string? ValidationError = null);
public sealed record RecoveryDialogContext(
    string CurrentPath,
    IReadOnlyList<string> PreservedPaths,
    Func<Task<RecoveryReview>> ReviewCurrent,
    Func<string, Task> KeepCurrent,
    Action? Resolved = null);
