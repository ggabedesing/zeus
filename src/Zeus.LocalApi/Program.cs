using System.Net;
using System.Security.Cryptography;
using System.Text;
using Zeus.LocalApi;

var secret = Environment.GetEnvironmentVariable("ZEUS_TOKEN");
if (secret is null || secret.Length < 32 || secret.Length > 256)
    throw new InvalidOperationException("ZEUS_TOKEN precisa conter de 32 a 256 caracteres.");
if (!int.TryParse(Environment.GetEnvironmentVariable("ZEUS_PORT"), out var port) || port is < 1024 or > 65535)
    throw new InvalidOperationException("ZEUS_PORT inválida.");
var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);
    options.Limits.MaxRequestBodySize = 4096;
    options.Limits.MaxRequestHeaderCount = 24;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<DesktopService>();
builder.Logging.ClearProviders();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    var supplied = context.Request.Headers["X-Zeus-Token"];
    // No CORS: the renderer talks to an allowlisted main-process bridge.
    if (supplied.Count != 1 || supplied[0] is not { Length: >= 32 and <= 256 } token ||
        !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(token))) ||
        context.Request.Host.Host != "127.0.0.1" || context.Request.Headers.ContainsKey("Origin"))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { message = "Acesso local não autorizado." });
        return;
    }
    try { await next(context); }
    catch (BadHttpRequestException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "Pedido inválido." }); }
    catch (OperationCanceledException) { if (!context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 504; await context.Response.WriteAsJsonAsync(new { message = "A leitura excedeu o prazo. Tente novamente." }); } }
    catch (Exception) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { message = "Não foi possível concluir. Seu histórico preserva alterações iniciadas." }); }
});
app.MapGet("/api/health", () => new { state = "ready", version = "0.2.0", localOnly = true });
app.MapGet("/api/diagnostics", (DesktopService service, CancellationToken token) => service.DiagnosticsAsync(token));
app.MapGet("/api/performance", (DesktopService service, CancellationToken token) => service.PerformanceAsync(token));
app.MapGet("/api/history", (DesktopService service, CancellationToken token) => service.HistoryAsync(token));
app.MapPost("/api/personalization/preview", (PreviewRequest request, DesktopService service, CancellationToken token) => service.PreviewAsync(request.LayoutId, token));
app.MapPost("/api/personalization/apply", (ApplyRequest request, DesktopService service, CancellationToken token) => service.ApplyAsync(request.PreviewId, token));
app.MapPost("/api/personalization/revert", (RevertRequest request, DesktopService service, CancellationToken token) => service.RevertAsync(request.TransactionId, token));
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("ZEUS_READY"));
await app.RunAsync();

record PreviewRequest(string LayoutId);
record ApplyRequest(Guid PreviewId);
record RevertRequest(Guid TransactionId);
