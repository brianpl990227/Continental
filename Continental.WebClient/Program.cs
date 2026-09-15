using Continental.Shared.Services;
using Continental.WebClient;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<IGameHost, NoGameHost>();
builder.Services.AddSingleton<IRoomDiscovery, NoRoomDiscovery>();
builder.Services.AddSingleton<IGameJoiner, WebSocketJoiner>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<GameAudio>();
builder.Services.AddScoped<CardFlight>();
builder.Services.AddScoped<Confetti>();
builder.Services.AddSingleton<IAppVersion, WebAppVersion>();
builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<UpdateChecker>();

await builder.Build().RunAsync();
