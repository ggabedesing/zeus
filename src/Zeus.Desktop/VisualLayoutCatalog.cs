using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Desktop;

internal static class VisualLayoutCatalog
{
    private const int CurrentSchemaVersion = 1;
    private const int MaxCatalogBytes = 64 * 1024;
    private const int MaxCustomPresets = 20;
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
        if (Encoding.UTF8.GetByteCount(json) > MaxCatalogBytes)
            throw new JsonException("O arquivo do catálogo visual excede o limite de 64 KB.");
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
                string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 48 || !names.Add(preset.Name) ||
                string.IsNullOrWhiteSpace(preset.Description) || preset.Description.Length > 240 ||
                !string.Equals(preset.Scope, "zeus-ui", StringComparison.Ordinal) ||
                !Enum.IsDefined(preset.Theme) || !Enum.IsDefined(preset.Accent))
                throw new JsonException("O catálogo contém perfil inválido, duplicado ou fora do escopo da interface ZEUS.");
        }

        return manifest.Presets.ToArray();
    }

    internal static IReadOnlyList<VisualLayoutPreset> ParseCustom(string json, IReadOnlyCollection<VisualLayoutPreset> builtIns)
    {
        var presets = Parse(json);
        if (presets.Count > MaxCustomPresets)
            throw new JsonException($"O catálogo pode conter no máximo {MaxCustomPresets} perfis personalizados.");
        var ids = builtIns.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
        var names = builtIns.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (!preset.Id.StartsWith("custom-", StringComparison.Ordinal) || preset.Id.Length > 60 ||
                preset.Id.Length <= "custom-".Length || preset.Id.Any(character =>
                    !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
                preset.Id.EndsWith("-", StringComparison.Ordinal) ||
                !ids.Add(preset.Id) || !names.Add(preset.Name))
                throw new JsonException("Perfis personalizados precisam de ID custom- único, nome distinto e caracteres ASCII simples.");
        }
        return presets;
    }

    internal static string SerializeCustom(IEnumerable<VisualLayoutPreset> presets) =>
        JsonSerializer.Serialize(new VisualLayoutManifest(CurrentSchemaVersion, presets.ToList()), Options);

    internal static string CreateTemplate() => SerializeCustom([
        new VisualLayoutPreset("custom-meu-tema", "Meu tema", DesktopTheme.Complete,
            AppAccentColor.ThemeDefault, "Meu perfil de cores do ZEUS.", "zeus-ui")
    ]);

    private sealed record VisualLayoutManifest(int SchemaVersion, List<VisualLayoutPreset>? Presets);
}
