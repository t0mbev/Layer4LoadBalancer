using LoadBalancer;
using LoadBalancer.BackendSelectors;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IBackendManager, BackendManager>();
builder.Services.AddSingleton<IBackendSelector, RoundRobinSelector>();
builder.Services.AddHostedService<ClientListener>();

var host = builder.Build();
host.Run();
