using GaifulinLab.Application;
using GaifulinLab.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPdfExportWorker(builder.Configuration);

await builder.Build().RunAsync();
