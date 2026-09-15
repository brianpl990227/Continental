using Continental.Server;
using Continental.Services;
using Continental.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Continental
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            builder.Services.AddSingleton<IGameHost, MauiGameHost>();
            builder.Services.AddSingleton<IRoomDiscovery, UdpRoomDiscovery>();
            builder.Services.AddSingleton<IGameJoiner, WebSocketJoiner>();
            builder.Services.AddScoped<AppState>();
            builder.Services.AddScoped<GameAudio>();
            builder.Services.AddScoped<CardFlight>();
            builder.Services.AddScoped<Confetti>();
            builder.Services.AddSingleton<IAppVersion, MauiAppVersion>();
            builder.Services.AddSingleton(_ => new HttpClient());
            builder.Services.AddScoped<UpdateChecker>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
