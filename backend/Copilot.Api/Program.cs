using Copilot.Ai;
using Copilot.Api.Auth;
using Copilot.Api.Cors;
using Copilot.Api.Endpoints;
using Copilot.Api.Hosting;
using Copilot.Api.RateLimiting;
using Copilot.Gorgias;
using Copilot.Pipeline;

var builder = WebApplication.CreateBuilder(args);

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
