namespace Zeus.Core;

/// <summary>
/// Validates the complete selection before execution. Windows-specific execution
/// stays outside this library; this order never implies concurrent operations.
/// </summary>
public static class MaintenancePolicy
{
    public static IReadOnlyList<MaintenanceActionId> ValidateAndOrder(IEnumerable<MaintenanceActionId> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var selected = new HashSet<MaintenanceActionId>();
        foreach (var action in actions)
        {
            _ = MaintenanceCatalog.Get(action);
            if (!selected.Add(action))
            {
                throw new ArgumentException("O plano contém uma ação repetida.", nameof(actions));
            }
        }

        if (selected.Count == 0)
        {
            throw new ArgumentException("Selecione pelo menos uma ação para iniciar a manutenção.", nameof(actions));
        }

        return Array.AsReadOnly(selected.OrderBy(GetOrder).ToArray());
    }

    public static bool RequiresRestorePoint(IEnumerable<MaintenanceActionId> actions) =>
        ValidateAndOrder(actions).Any(action => MaintenanceCatalog.Get(action).RequiresRestorePoint);

    public static void EnsureRestorePoint(IEnumerable<MaintenanceActionId> actions, bool restorePointConfirmed)
    {
        if (RequiresRestorePoint(actions) && !restorePointConfirmed)
        {
            throw new InvalidOperationException(
                "O reparo foi bloqueado porque a proteção de recuperação não foi confirmada. As verificações continuam disponíveis em outro plano.");
        }
    }

    private static int GetOrder(MaintenanceActionId action) => action switch
    {
        MaintenanceActionId.ScanWindowsImage => 0,
        MaintenanceActionId.RepairWindowsImage => 1,
        MaintenanceActionId.VerifySystemFiles => 2,
        MaintenanceActionId.RepairSystemFiles => 3,
        MaintenanceActionId.AnalyzeSystemDrive => 4,
        MaintenanceActionId.DefenderQuickScan => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Ação desconhecida.")
    };
}
