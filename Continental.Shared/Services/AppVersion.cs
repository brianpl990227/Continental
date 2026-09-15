using System.Net.Http.Headers;
using System.Text.Json;

namespace Continental.Shared.Services;

public sealed record UpdateInfo(string Version, string Url, bool IsNewer);

public interface IAppVersion
{
    string Display { get; }

    string? Build { get; }

    bool Installable { get; }

    Task OpenAsync(string url);
}

public sealed class WebAppVersion : IAppVersion
{
    public string Display => "navegador";

    public string? Build => null;

    public bool Installable => false;

    public Task OpenAsync(string url) => Task.CompletedTask;
}

public sealed class UpdateChecker(IAppVersion version, HttpClient http)
{
    private const string Repository = "brianpl990227/Continental";

    public string Current => version.Display;

    public string? Build => version.Build;

    public bool Installable => version.Installable;

    public UpdateInfo? Latest { get; private set; }

    public bool Checked { get; private set; }

    public async Task CheckAsync(CancellationToken token = default)
    {
        if (Checked)
            return;

        Checked = true;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("Continental");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            using var response = await http.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
                return;

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            if (!json.RootElement.TryGetProperty("tag_name", out var tag))
                return;

            var name = tag.GetString();

            if (string.IsNullOrWhiteSpace(name))
                return;

            var url = json.RootElement.TryGetProperty("html_url", out var link)
                ? link.GetString() ?? $"https://github.com/{Repository}/releases/latest"
                : $"https://github.com/{Repository}/releases/latest";

            Latest = new UpdateInfo(name.TrimStart('v', 'V'), url, IsNewer(name, version.Display));
        }
        catch (Exception)
        {
        }
    }

    public Task OpenLatestAsync()
        => Latest is null ? Task.CompletedTask : version.OpenAsync(Latest.Url);

    internal static bool IsNewer(string tag, string current)
    {
        var a = Parse(tag);
        var b = Parse(current);

        if (a.Length == 0 || b.Length == 0)
            return false;

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : 0;
            var right = i < b.Length ? b[i] : 0;

            if (left != right)
                return left > right;
        }

        return false;
    }

    private static int[] Parse(string value)
    {
        var trimmed = value.Trim().TrimStart('v', 'V');
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>(parts.Length);

        foreach (var part in parts)
        {
            var digits = new string(part.TakeWhile(char.IsDigit).ToArray());

            if (digits.Length == 0)
                break;

            numbers.Add(int.Parse(digits));
        }

        return numbers.ToArray();
    }
}
