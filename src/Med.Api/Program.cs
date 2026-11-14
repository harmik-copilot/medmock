using System.Text.RegularExpressions;
using Med.Api;
using Med.Domain;
using Med.Ingest;
using Med.Ingest.Simulators;
using Med.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------------------------
// Layers, lowest to highest priority: appsettings.json -> appsettings.{Environment}.json -> environment variables.
// Environment is chosen by ASPNETCORE_ENVIRONMENT (Development, Test, Staging, Production).
// Secrets are NEVER committed. Outside Development the shared QA password must be supplied, normally via the
// environment variable Auth__QaPassword (from a pipeline secret or key vault). The app refuses to start without it.
var qaPassword = builder.Configuration["Auth:QaPassword"];
if (string.IsNullOrWhiteSpace(qaPassword))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"Auth:QaPassword is not set for the '{builder.Environment.EnvironmentName}' environment. " +
            "Provide it with the environment variable Auth__QaPassword.");
    }
    qaPassword = "test-pass";   // local development only
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStudyStore, InMemoryStudyStore>();
builder.Services.AddSingleton<IAuditLog, InMemoryAuditLog>();
builder.Services.AddSingleton<IPacsSource, FakePacsSource>();
builder.Services.AddSingleton<AccessPolicy>();
builder.Services.AddSingleton<IngestService>();
builder.Services.AddSingleton(new AuthService(qaPassword));
builder.Services.AddSingleton<ProviderDirectory>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---- Optional seed data -------------------------------------------------------------------
// Seed:OnStartup is true locally and in Test, false in Staging (where data arrives through the ingest API,
// as it would in production). Tests must not depend on seed data being present.
if (app.Configuration.GetValue<bool>("Seed:OnStartup", true))
{
    var directory = app.Services.GetRequiredService<ProviderDirectory>();
    var source = app.Services.GetRequiredService<IPacsSource>();
    var ingest = app.Services.GetRequiredService<IngestService>();
    foreach (var provider in directory.All())
        ingest.Ingest(provider, source.Fetch(provider.Id));
}

// ---- Cross-cutting: correlation id and basic security headers --------------------------------
// Every response carries X-Correlation-ID (echoing a safe client-supplied value, otherwise a new one).
// Quote it in bug reports so engineers can find the exact request in the logs.
var safeCorrelationId = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || !safeCorrelationId.IsMatch(correlationId))
        correlationId = Guid.NewGuid().ToString("N");

    context.Response.Headers["X-Correlation-ID"] = correlationId;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

// ---- Operations ----------------------------------------------------------------------------
// Used by deployments (wait until ready) and by smoke tests (is this the environment I think it is?).
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapGet("/version", (IWebHostEnvironment env, IConfiguration config) => Results.Ok(new
{
    service = "Med.Api",
    version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev",
    environment = env.EnvironmentName,
    region = config["Deployment:Region"] ?? "ANZ"
}));

// ---- Auth ----
app.MapPost("/auth/login", (LoginRequest request, AuthService auth) =>
{
    var token = auth.Login(request.Username, request.Password);
    return token is null ? Results.Unauthorized() : Results.Ok(new { token });
});

// ---- SHARE: list the studies this user is allowed to see ----
app.MapGet("/studies", (HttpContext ctx, AuthService auth, IStudyStore store, AccessPolicy policy) =>
{
    var user = auth.Resolve(ctx.Request);
    if (user is null) return Results.Unauthorized();

    var visible = store.All()
        .Where(s => policy.Evaluate(user, s).Allowed)
        .Select(StudySummary.From)
        .ToList();
    return Results.Ok(visible);
});

// ---- SHARE: open one study. Every attempt, allowed or not, is audited. ----
app.MapGet("/studies/{id:guid}", (Guid id, HttpContext ctx, AuthService auth, IStudyStore store,
    AccessPolicy policy, IAuditLog audit, TimeProvider clock) =>
{
    var user = auth.Resolve(ctx.Request);
    if (user is null) return Results.Unauthorized();

    var study = store.Get(id);
    if (study is null)
    {
        audit.Write(new AuditEntry(clock.GetUtcNow(), user.Id, "OPEN_STUDY", id, false, "not found"));
        return Results.NotFound();
    }

    var decision = policy.Evaluate(user, study);
    audit.Write(new AuditEntry(clock.GetUtcNow(), user.Id, "OPEN_STUDY", id, decision.Allowed, decision.Reason));

    // The response body never says WHY access was denied; the reason lives only in the audit log.
    return decision.Allowed
        ? Results.Ok(StudyDetail.From(study))
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});

// ---- ACQUIRE (pull): a provider re-pulls its own PACS feed. Safe to run repeatedly. ----
app.MapPost("/ingest/run", (HttpContext ctx, AuthService auth, ProviderDirectory directory,
    IPacsSource source, IngestService ingest) =>
{
    var user = auth.Resolve(ctx.Request);
    if (user is null) return Results.Unauthorized();
    if (user.Role != Role.Provider || user.ProviderId is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var provider = directory.Find(user.ProviderId);
    if (provider is null) return Results.NotFound();

    return Results.Ok(ingest.Ingest(provider, source.Fetch(provider.Id)));
});

// ---- ACQUIRE (push): a clinic's connector sends studies. The region always comes from the provider. ----
app.MapPost("/ingest/studies", (IngestStudiesRequest request, HttpContext ctx, AuthService auth,
    ProviderDirectory directory, IngestService ingest) =>
{
    var user = auth.Resolve(ctx.Request);
    if (user is null) return Results.Unauthorized();
    if (user.Role != Role.Provider || user.ProviderId is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var provider = directory.Find(user.ProviderId);
    if (provider is null) return Results.NotFound();

    var items = request.Studies;
    if (items is null || items.Count == 0)
        return Results.BadRequest(new { error = "studies must contain at least one study" });
    if (items.Count > 100)
        return Results.BadRequest(new { error = "at most 100 studies per request" });

    var incoming = new List<IncomingStudy>();
    foreach (var s in items)
    {
        byte[] pixels;
        try
        {
            pixels = Convert.FromBase64String(s.PixelsBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "pixelsBase64 is not valid base64" });
        }

        var header = new DicomHeader(s.StudyInstanceUid, s.PatientId, s.PatientName, s.Modality,
            s.BodyPart, s.StudyDate, s.SeriesCount);
        incoming.Add(new IncomingStudy(header, s.ReferrerId, pixels));
    }

    return Results.Ok(ingest.Ingest(provider, incoming));
});

// ---- Audit trail, scoped to the provider's own studies ----
app.MapGet("/audit", (HttpContext ctx, AuthService auth, IAuditLog audit, IStudyStore store) =>
{
    var user = auth.Resolve(ctx.Request);
    if (user is null) return Results.Unauthorized();
    if (user.Role != Role.Provider || user.ProviderId is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var entries = audit.All()
        .Where(e => e.StudyId is Guid sid && store.Get(sid)?.ProviderId == user.ProviderId)
        .ToList();
    return Results.Ok(entries);
});

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the test project.
public partial class Program { }
