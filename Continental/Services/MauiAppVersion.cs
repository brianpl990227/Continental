using Continental.Shared.Services;

namespace Continental.Services;

public sealed class MauiAppVersion : IAppVersion
{
    public string Display => AppInfo.VersionString;

    public string? Build => AppInfo.BuildString;

    public bool Installable => true;

    public async Task OpenAsync(string url)
    {
        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
        }
    }
}
