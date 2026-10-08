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

        EnsureScansAndRepairsAreSeparate(selected, nameof(actions));

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

    public static IReadOnlyList<MaintenanceRequest> ValidateRequests(IEnumerable<MaintenanceRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var supplied = requests.ToArray();
        if (supplied.Any(request => request is null))
            throw new ArgumentException("O plano contém uma solicitação vazia.", nameof(requests));
        if (supplied.Length == 0)
            throw new ArgumentException("Selecione pelo menos uma ação para iniciar a manutenção.", nameof(requests));
        var selectedActions = new HashSet<MaintenanceActionId>();
        var driverTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rollbackTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in supplied)
        {
            _ = MaintenanceCatalog.Get(request.Action);
            if (request.Action == MaintenanceActionId.InstallDriverUpdate)
            {
                if (!MaintenanceRequestProtocol.TryParseDriverIdentity(request.TargetId, out var id, out var revision))
                    throw new ArgumentException("Selecione a identidade exata do driver oferecido pelo Windows Update.", nameof(requests));
                if (!WindowsUpdateSourcePolicy.IsAllowed(request.UpdateServerSelection, request.UpdateServiceId))
                    throw new ArgumentException("A instalação de driver exige uma origem do Windows Update Agent reconhecida e permitida.", nameof(requests));
                if (!driverTargets.Add(id.ToString("D") + ":" + revision))
                    throw new ArgumentException("O plano contém a mesma identidade de driver mais de uma vez.", nameof(requests));
            }
            else if (request.Action == MaintenanceActionId.RollbackDriver)
            {
                if (!MaintenanceRequestProtocol.TryParsePnpInstanceId(request.TargetId) || request.EulaAccepted)
                    throw new ArgumentException("A reversão exige a identidade PnP de um dispositivo presente e não aceita dados de licença.", nameof(requests));
                if (!rollbackTargets.Add(request.TargetId!))
                    throw new ArgumentException("O plano contém a mesma identidade de dispositivo mais de uma vez.", nameof(requests));
            }
            else
            {
                if (!selectedActions.Add(request.Action))
                    throw new ArgumentException("O plano contém uma ação repetida.", nameof(requests));
                if (request.TargetId is not null || request.EulaAccepted || request.UpdateServerSelection is not null || request.UpdateServiceId is not null)
                    throw new ArgumentException("Somente a instalação de driver permite identidade e aceite de licença.", nameof(requests));
            }
        }
        if (driverTargets.Count > 0 && supplied.Length != 1)
            throw new ArgumentException("A instalação de driver precisa ser revisada e executada em uma sessão exclusiva.", nameof(requests));
        if (rollbackTargets.Count > 0 && supplied.Length != 1)
            throw new ArgumentException("A reversão de driver precisa ser revisada e executada em uma sessão exclusiva.", nameof(requests));
        EnsureScansAndRepairsAreSeparate(supplied.Select(request => request.Action), nameof(requests));
        // OrderBy is stable: drivers retain the explicit order selected by the user.
        return Array.AsReadOnly(supplied.OrderBy(request => GetOrder(request.Action)).ToArray());
    }

    private static void EnsureScansAndRepairsAreSeparate(IEnumerable<MaintenanceActionId> actions, string parameterName)
    {
        var selected = actions.ToHashSet();
        var includesScan = selected.Contains(MaintenanceActionId.ScanWindowsImage) ||
                           selected.Contains(MaintenanceActionId.VerifySystemFiles);
        var includesRepair = selected.Contains(MaintenanceActionId.RepairWindowsImage) ||
                             selected.Contains(MaintenanceActionId.RepairSystemFiles);
        if (includesScan && includesRepair)
            throw new ArgumentException(
                "Separe verificação e reparo: execute a análise primeiro, revise o resultado e então monte um novo plano de reparo.",
                parameterName);
    }

    private static int GetOrder(MaintenanceActionId action) => action switch
    {
        MaintenanceActionId.ScanWindowsImage => 0,
        MaintenanceActionId.RepairWindowsImage => 1,
        MaintenanceActionId.VerifySystemFiles => 2,
        MaintenanceActionId.RepairSystemFiles => 3,
        MaintenanceActionId.AnalyzeSystemDrive => 4,
        MaintenanceActionId.OptimizeSystemDrive => 5,
        MaintenanceActionId.InstallDriverUpdate => 6,
        MaintenanceActionId.UpdateDefenderSignatures => 7,
        MaintenanceActionId.DefenderQuickScan => 8,
        MaintenanceActionId.DefenderFullScan => 9,
        MaintenanceActionId.DefenderOfflineScan => 10,
        MaintenanceActionId.RollbackDriver => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Ação desconhecida.")
    };
}
