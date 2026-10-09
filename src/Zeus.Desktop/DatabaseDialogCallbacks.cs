namespace Zeus.Desktop;

/// <summary>Testable boundary around the interactive file and restart steps for database recovery.</summary>
internal sealed record DatabaseDialogCallbacks(
    Func<string?> ChooseBackupPath,
    Func<string?> ChooseRestorePath,
    Func<string, bool> ConfirmRestore,
    Action RestartAfterRestore);
