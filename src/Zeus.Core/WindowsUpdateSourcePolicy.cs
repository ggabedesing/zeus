namespace Zeus.Core;

/// <summary>Recognizes the WUA source modes supported by ZEUS and describes their evidence limits.</summary>
public static class WindowsUpdateSourcePolicy
{
    public const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    public static bool IsAllowed(int? serverSelection, string? serviceId) =>
        serverSelection is 0 or 1 or 2 && serviceId is null ||
        serverSelection == 3 && string.Equals(serviceId, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase);

    public static string Describe(int? serverSelection, string? serviceId) => (serverSelection, serviceId) switch
    {
        (0, null) => "Windows Update Agent · servidor padrão (origem efetiva desconhecida)",
        (1, null) => "Windows Update Agent · servidor gerenciado",
        (2, null) => "Windows Update · serviço público",
        (3, var id) when string.Equals(id, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase) =>
            "Microsoft Update · fonte lógica oficial; servidor efetivo desconhecido",
        (3, var id) when MaintenanceRequestProtocol.TryParseUpdateServiceId(id) =>
            $"Windows Update Agent · serviço adicional não permitido ({id})",
        _ => "Windows Update Agent · origem indisponível"
    };
}
