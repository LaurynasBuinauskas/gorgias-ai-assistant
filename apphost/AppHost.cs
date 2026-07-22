var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Copilot_Api>("api");

builder.AddViteApp("panel", "../panel")
    .WithPnpm()
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"));

builder.Build().Run();
