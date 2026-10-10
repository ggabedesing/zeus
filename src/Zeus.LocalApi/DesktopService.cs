using System.Collections.Concurrent;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.LocalApi;

public sealed class DesktopService
{
    private readonly SemaphoreSlim inventoryGate = new(1, 1);
    private readonly SemaphoreSlim observationGate = new(1, 1);
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, PreparedPreview> previews = new();
    private readonly UserOptimizationService changes;
    private readonly string imageRoot;
    private HardwareSnapshot? inventory;
    private PerformanceObservation? observation;
    private static readonly IReadOnlyDictionary<string, (string Name, uint Accent, uint Background)> Layouts =
        new Dictionary<string, (string, uint, uint)>(StringComparer.Ordinal)
        {
            ["windows-moderno"] = ("Windows Moderno", 0x3b5bff, 0x0f1117),
            ["macos-inspired"] = ("macOS Inspired", 0x0a84ff, 0x000000),
            ["minimalista"] = ("Minimalista", 0x6366f1, 0x09090b),
            ["gamer-neon"] = ("Gamer Neon", 0x00f0ff, 0x05050a)
        };

    public DesktopService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "desktop-ui");
        imageRoot = Path.Combine(root, "wallpapers");
        // The existing engine rejects redirected storage ancestors and checks backup files.
        changes = new UserOptimizationService(Path.Combine(root, "changes"));
    }

    public async Task<HardwareSnapshot> DiagnosticsAsync(CancellationToken token)
    {
        await inventoryGate.WaitAsync(token);
        try
        {
            if (inventory is not null && DateTimeOffset.UtcNow - inventory.CollectedAt < TimeSpan.FromMinutes(5)) return inventory;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(55));
            inventory = await new WindowsHardwareDiagnostics().CollectAsync(deadline.Token);
            return inventory;
        }
        finally { inventoryGate.Release(); }
    }

    public async Task<PerformanceObservation> PerformanceAsync(CancellationToken token)
    {
        await observationGate.WaitAsync(token);
        try
        {
            if (observation is not null && DateTimeOffset.UtcNow - observation.CollectedAt < TimeSpan.FromSeconds(4)) return observation;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            observation = await new WindowsPerformanceProbe().SampleAsync(TimeSpan.FromSeconds(1), deadline.Token);
            return observation;
        }
        finally { observationGate.Release(); }
    }

    public async Task<object> PreviewAsync(string? layoutId, CancellationToken token)
    {
        if (layoutId is null || !Layouts.TryGetValue(layoutId, out var layout))
            return new { id = Guid.Empty, summary = "Visual desconhecido.", changes = Array.Empty<string>(), limitations = Array.Empty<string>(), canApply = false };
        foreach (var pair in previews.Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow)) previews.TryRemove(pair.Key, out _);
        if (previews.Count >= 32) throw new InvalidOperationException("Prévias demais.");
        var id = Guid.NewGuid();
        var canApply = false;
        var reason = "";
        try
        {
            var state = await changes.ReadWallpaperMonitorDiscoveryAsync(token);
            canApply = !state.IsSlideshowConfigured && state.Position is not null && state.Monitors.Count > 0 &&
                       state.Monitors.All(monitor => File.Exists(monitor.Path) && new FileInfo(monitor.Path).Length is > 0 and <= 32 * 1024 * 1024);
            if (!canApply) reason = "O fundo atual não pôde ser salvo com segurança ou usa apresentação de slides. Nenhuma alteração será feita.";
        }
        catch (OperationCanceledException) { throw; }
        catch { reason = "O Windows não confirmou uma imagem estática para restaurar. Nenhuma alteração será feita."; }
        var image = WallpaperImage.Create(layout.Accent, layout.Background);
        if (canApply) previews[id] = new(layoutId, DateTimeOffset.UtcNow.AddMinutes(5), image);
        return new
        {
            id,
            summary = $"Aplicar o papel de parede {layout.Name}",
            changes = new[] { "Gerar um papel de parede estático com as cores deste visual.", "Salvar uma cópia dos fundos anteriores e aplicar em todos os monitores.", "Verificar o resultado e permitir restaurar pelo Histórico." },
            limitations = new[] { "Esta versão aplica apenas o papel de parede. Dock, widgets, relógio, barra de tarefas e animações da prévia são ilustrativos.", "Ferramentas externas não serão instaladas. A segurança do Windows será preservada.", reason }.Where(text => text.Length > 0).ToArray(),
            canApply
            , wallpaperDataUrl = "data:image/bmp;base64," + Convert.ToBase64String(image)
        };
    }

    public async Task<object> ApplyAsync(Guid previewId, CancellationToken token)
    {
        await mutationGate.WaitAsync(token);
        try
        {
            if (!previews.TryRemove(previewId, out var preview) || preview.ExpiresAt <= DateTimeOffset.UtcNow)
                return new { id = Guid.Empty, state = "blocked", message = "A prévia expirou ou já foi usada. Abra uma nova prévia antes de aplicar." };
            AssertPlainDirectory(imageRoot);
            Directory.CreateDirectory(imageRoot);
            AssertPlainDirectory(imageRoot);
            var image = Path.Combine(imageRoot, $"zeus-{preview.LayoutId}-{previewId:N}.bmp");
            using (var stream = new FileStream(image, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(preview.Image);
                stream.Flush(true);
            }
            // Cancellation may stop preparation, never abandon a started mutation on a UI disconnect.
            token.ThrowIfCancellationRequested();
            var result = await changes.ApplyWallpaperAsync(image, null, WallpaperPosition.Fill, CancellationToken.None);
            var saved = (await changes.ListChangesAsync(CancellationToken.None)).FirstOrDefault(item => item.Id == result.SessionId);
            return new { id = result.SessionId, state = saved is null ? (result.Succeeded ? "applied" : "blocked") : State(saved.Status), message = result.Message };
        }
        finally { mutationGate.Release(); }
    }

    public async Task<object> RevertAsync(Guid transactionId, CancellationToken token)
    {
        await mutationGate.WaitAsync(token);
        try
        {
            if (transactionId == Guid.Empty || !(await changes.ListChangesAsync(token)).Any(item => item.Id == transactionId))
                return new { id = transactionId, state = "blocked", message = "Esta alteração não está no histórico local." };
            token.ThrowIfCancellationRequested();
            var result = await changes.RestoreAsync(transactionId, CancellationToken.None);
            var saved = (await changes.ListChangesAsync(CancellationToken.None)).FirstOrDefault(item => item.Id == result.SessionId);
            return new { id = result.SessionId, state = saved is null ? "blocked" : State(saved.Status), message = result.Message };
        }
        finally { mutationGate.Release(); }
    }

    public async Task<object[]> HistoryAsync(CancellationToken token)
    {
        var history = await changes.ListChangesAsync(token);
        return history.OrderByDescending(item => item.CreatedAt).Take(200).Select(item => (object)new
        {
            id = item.Id,
            layoutId = Layouts.Keys.FirstOrDefault(id => item.Description.Contains($"zeus-{id}-", StringComparison.Ordinal)) ?? "unknown",
            createdAt = item.CreatedAt,
            state = State(item.Status),
            message = item.StatusText
        }).ToArray();
    }

    private static void AssertPlainDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Pasta redirecionada não aceita.");
    }
    private static string State(UserChangeStatus status) => status switch
    {
        UserChangeStatus.Applied => "applied",
        UserChangeStatus.Restored => "restored",
        UserChangeStatus.RestoreBlocked => "blocked",
        _ => "needs-review"
    };
    private sealed record PreparedPreview(string LayoutId, DateTimeOffset ExpiresAt, byte[] Image);
}
