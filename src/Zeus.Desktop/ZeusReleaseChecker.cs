using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace Zeus.Desktop;

internal sealed record ZeusReleaseCheckResult(string Summary, Uri? ReleasePage);

internal sealed class ZeusReleaseChecker
{
    private static readonly Uri ApiUri = new("https://api.github.com/repos/ggabedesing/zeus/releases/latest");
    private static readonly Uri OfficialReleasePage = new("https://github.com/ggabedesing/zeus/releases");
    private readonly HttpMessageHandler? _testHandler;

    internal ZeusReleaseChecker() { }
    internal ZeusReleaseChecker(HttpMessageHandler testHandler) => _testHandler = testHandler;

    public async Task<ZeusReleaseCheckResult> CheckAsync(string installedVersion, CancellationToken cancellationToken)
    {
        using var client = _testHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(12) }
            : new HttpClient(_testHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZEUS", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.GetAsync(ApiUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new("Ainda não há uma versão estável publicada no GitHub para consulta.", OfficialReleasePage);
        if (!response.IsSuccessStatusCode)
            return new($"Não foi possível consultar as versões do ZEUS (HTTP {(int)response.StatusCode}).", null);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        var html = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;
        var prerelease = root.TryGetProperty("prerelease", out var preValue) && preValue.ValueKind == JsonValueKind.True;
        var page = Uri.TryCreate(html, UriKind.Absolute, out var parsed) &&
                   parsed.Scheme == Uri.UriSchemeHttps && parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                   parsed.AbsolutePath.StartsWith("/ggabedesing/zeus/releases/", StringComparison.OrdinalIgnoreCase)
            ? parsed
            : OfficialReleasePage;

        if (prerelease) return new($"A versão mais recente publicada é de pré-lançamento ({tag ?? "versão sem identificação"}); confira os detalhes antes de instalar.", page);
        if (string.IsNullOrWhiteSpace(tag) || !TryVersion(tag, out var latest) || !TryVersion(installedVersion, out var installed))
            return new("A versão foi encontrada, mas não foi possível comparar os números com segurança. Confira os detalhes da publicação.", page);
        return latest > installed
            ? new($"Há uma versão mais recente: {tag}. Esta instalação: {installedVersion}.", page)
            : new($"Nenhuma versão mais recente encontrada. Esta instalação: {installedVersion}; publicação estável: {tag}.", page);
    }

    private static bool TryVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V').Split('+', '-', StringSplitOptions.TrimEntries)[0];
        if (Version.TryParse(normalized, out var parsed))
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0);
        return false;
    }
}
