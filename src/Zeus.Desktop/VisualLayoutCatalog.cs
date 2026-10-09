using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeus.Desktop;

internal static class VisualLayoutCatalog
{
    private const int CurrentSchemaVersion = 1;
    private const string ResourceName = "Zeus.Desktop.visual-layouts.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    public static IReadOnlyList<VisualLayoutPreset> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("O catálogo de perfis visuais não foi incluído no aplicativo.");
        using var document = JsonDocument.Parse(stream);
        return Parse(document.RootElement.GetRawText());
    }

    internal static IReadOnlyList<VisualLayoutPreset> Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<VisualLayoutManifest>(json, Options)
            ?? throw new JsonException("O catálogo de perfis visuais está vazio.");
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException($"Versão de catálogo visual não suportada: {manifest.SchemaVersion}.");
        if (manifest.Presets is not { Count: > 0 })
            throw new JsonException("O catálogo visual precisa conter ao menos um perfil.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in manifest.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id) ||
                string.IsNullOrWhiteSpace(preset.Name) || !names.Add(preset.Name) ||
                string.IsNullOrWhiteSpace(preset.Description) ||
                !string.Equals(preset.Scope, "zeus-ui", StringComparison.Ordinal) ||
                !Enum.IsDefined(preset.Theme) || !Enum.IsDefined(preset.Accent))
                throw new JsonException("O catálogo contém perfil inválido, duplicado ou fora do escopo da interface ZEUS.");
        }

        return manifest.Presets.ToArray();
    }

    private sealed record VisualLayoutManifest(int SchemaVersion, List<VisualLayoutPreset>? Presets);
}
