using Copilot.Ai;
using Copilot.Api.Auth;
using Copilot.Api.Contracts;
using Copilot.Api.Cors;
using Copilot.Api.Endpoints;
using Copilot.Api.Hosting;
using Copilot.Api.RateLimiting;
using Copilot.Gorgias;
using Copilot.Pipeline;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DraftLimitsOptions>()
    .BindConfiguration(DraftLimitsOptions.SectionName)
    .Validate(o => o.MaxTurns > 0, "DraftLimits:MaxTurns must be greater than zero.")
    .Validate(
        o => o.MaxTotalCharacters >= o.MaxTurnCharacters,
        "DraftLimits:MaxTotalCharacters cannot be smaller than a single turn's allowance.")
    .ValidateOnStart();

// Kestrel defaults to 30 MB. This API only ever accepts small JSON, so an oversized body is
// refused at the transport layer before any handler — or the model — sees it.
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var limits = new DraftLimitsOptions();
    context.Configuration.GetSection(DraftLimitsOptions.SectionName).Bind(limits);
    kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes;
});

builder.Services.AddGorgias();
builder.Services.AddAi();
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
