using Copilot.Ai;
using Copilot.Api.Auth;
using Copilot.Api.Contracts;
using Copilot.Api.Cors;
using Copilot.Api.Endpoints;
using Copilot.Api.Hosting;
using Copilot.Api.RateLimiting;
using Copilot.Gorgias;
using Copilot.Knowledge;
using Copilot.Pipeline;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ShellConfigOptions>()
    .BindConfiguration(ShellConfigOptions.SectionName);

builder.Services.AddOptions<DraftLimitsOptions>()
    .BindConfiguration(DraftLimitsOptions.SectionName)
    .Validate(o => o.MaxTurns > 0, "DraftLimits:MaxTurns must be greater than zero.")
    .Validate(
        o => o.MaxTotalCharacters >= o.MaxTurnCharacters,
        "DraftLimits:MaxTotalCharacters cannot be smaller than a single turn's allowance.")
    .ValidateOnStart();

// Kestrel defaults to 30 MB. This API only ever accepts small JSON, so an oversized body is
// refused at the transport layer before any handler — or the model — sees it.
//
// This is a backstop, not the main control: a request large enough to matter but small
// enough to parse is caught by DraftLimitsOptions and gets a clean 400 explaining which cap
// it broke. Verified against production — a 100 KB body returns 400, while anything over the
// limit below is refused before parsing and surfaces as 502, because App Service's front end
// reports Kestrel's connection abort rather than passing through 413.
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var limits = new DraftLimitsOptions();
    context.Configuration.GetSection(DraftLimitsOptions.SectionName).Bind(limits);
    kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes;
});

builder.Services.AddGorgias();
builder.Services.AddAi();
builder.Services.AddKnowledgeStore();
builder.Services.AddDraftingPipeline();
builder.Services.AddClientAddressForwarding();
builder.Services.AddApiProblemDetails();
builder.Services.AddApiRateLimiting();
builder.Services.AddBearerTokenAuthentication();
builder.Services.AddPanelCors(builder.Environment, builder.Configuration);

var app = builder.Build();

app.UseApiExceptionHandler();
app.UseForwardedHeaders();
app.UseRouting();
app.UseCors(CorsExtensions.PolicyName);
app.UseRateLimiter();
app.UseBearerTokenAuthentication();

app.MapGet("/", () => "Copilot API");
app.MapHealthEndpoints();
app.MapDraftEndpoints();
app.MapConfigEndpoints();
app.MapTelemetryEndpoints();

app.Run();
