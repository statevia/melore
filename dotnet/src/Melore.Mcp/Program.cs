using Melore.Core.Ports;
using Melore.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var settings = SettingsLoader.LoadFromArgs(args);
var builder = Host.CreateApplicationBuilder();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(sp =>
    MeloreRuntime.Create(settings, sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton(sp => sp.GetRequiredService<MeloreRuntime>().Search);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MeloreRuntime>().References);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MeloreRuntime>().Trace);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MeloreRuntime>().Explain);
builder.Services.AddSingleton<IMeloreStore>(sp => sp.GetRequiredService<MeloreRuntime>().Store);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
