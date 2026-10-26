namespace CursorShake.App.Dialogs;

public sealed record DiagnosticAction(string Label, string ConfirmationText, Func<Task> Execute);
