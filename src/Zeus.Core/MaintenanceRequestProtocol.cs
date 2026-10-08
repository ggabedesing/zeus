using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zeus.Core;

/// <summary>The complete elevated-helper input protocol. No arbitrary commands or paths are accepted.</summary>
public static class MaintenanceRequestProtocol
{
    public const int MaximumPayloadBytes = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Encode(IReadOnlyCollection<MaintenanceRequest> requests)
    {
        var validated = MaintenancePolicy.ValidateRequests(requests);
        var payload = JsonSerializer.SerializeToUtf8Bytes(validated.Select(request => new
        {
            Action = request.Action.ToString(), request.TargetId, request.EulaAccepted
        }));
        if (payload.Length > MaximumPayloadBytes)
            throw new ArgumentException("O plano excede o tamanho permitido.", nameof(requests));
        return Convert.ToBase64String(payload);
    }

    public static bool TryReadArguments(string[] arguments, out Guid sessionId,
        out IReadOnlyList<MaintenanceRequest> requests)
    {
        sessionId = Guid.Empty;
        requests = [];
        if (arguments.Length != 4 || arguments[0] != "--session" || arguments[1].Length != 36 ||
            !Guid.TryParseExact(arguments[1], "D", out sessionId) || sessionId == Guid.Empty)
            return false;
        if (arguments[2] == "--actions") return TryReadLegacy(arguments[3], out requests);
        if (arguments[2] != "--requests" || arguments[3].Length > ((MaximumPayloadBytes + 2) / 3) * 4)
            return false;
        try
        {
            var bytes = Convert.FromBase64String(arguments[3]);
            if (bytes.Length > MaximumPayloadBytes) return false;
            using var document = JsonDocument.Parse(StrictUtf8.GetString(bytes), new JsonDocumentOptions
            {
                MaxDepth = 4, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var selected = new List<MaintenanceRequest>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) return false;
                string? actionName = null;
                string? target = null;
                var accepted = false;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) return false;
                    switch (property.Name)
                    {
                        case "Action" when property.Value.ValueKind == JsonValueKind.String:
                            actionName = property.Value.GetString();
                            break;
                        case "TargetId" when property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null:
                            target = property.Value.GetString();
                            break;
                        case "EulaAccepted" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                            accepted = property.Value.GetBoolean();
                            break;
                        default:
                            return false;
                    }
                }
                if (!TryParseAction(actionName, out var action)) return false;
                selected.Add(new MaintenanceRequest(action, target, accepted));
            }
            requests = MaintenancePolicy.ValidateRequests(selected);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    public static bool TryParseDriverIdentity(string? target, out Guid updateId, out int revision)
    {
        updateId = Guid.Empty;
        revision = 0;
        if (target is null || target.Length is < 38 or > 47 || target[36] != ':' ||
            !Guid.TryParseExact(target[..36], "D", out updateId) || updateId == Guid.Empty)
            return false;
        var revisionText = target[37..];
        return revisionText.Length > 0 && revisionText[0] != '0' &&
            revisionText.All(char.IsAsciiDigit) &&
            int.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0;
    }

    private static bool TryReadLegacy(string input, out IReadOnlyList<MaintenanceRequest> requests)
    {
        requests = [];
        if (input.Length > 256) return false;
        var selected = new List<MaintenanceRequest>();
        foreach (var name in input.Split(','))
        {
            if (!TryParseAction(name, out var action)) return false;
            selected.Add(new MaintenanceRequest(action));
        }
        try { requests = MaintenancePolicy.ValidateRequests(selected); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool TryParseAction(string? name, out MaintenanceActionId action) =>
        Enum.TryParse(name, ignoreCase: false, out action) && Enum.IsDefined(action) && Enum.GetName(action) == name;
}
