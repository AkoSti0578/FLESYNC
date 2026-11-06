using FLEsync.Core;
using FLEsync.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = SettingsStore.ServiceName);
builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
host.Run();
