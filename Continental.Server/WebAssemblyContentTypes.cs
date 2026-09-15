using GenHTTP.Api.Content;
using GenHTTP.Api.Infrastructure;
using GenHTTP.Api.Protocol;
using GenHTTP.Modules.IO;

namespace Continental.Server;

public sealed class WebAssemblyContentTypes(IHandler inner, string webRoot) : IHandler
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wasm"] = "application/wasm",
        [".dat"] = "application/octet-stream",
        [".blat"] = "application/octet-stream",
        [".pdb"] = "application/octet-stream",
        [".dll"] = "application/octet-stream",
        [".webmanifest"] = "application/manifest+json"
    };

    private readonly string _root = Path.GetFullPath(webRoot);

    public ValueTask PrepareAsync(IServer server) => inner.PrepareAsync(server);

    public async ValueTask<IResponse?> HandleAsync(IRequest request)
    {
        if (Resolve(request.Header.Path.ToString()) is { } hit)
        {
            return request.Respond()
                          .Content(Resource.FromFile(hit.File).Build(), new ContentType(hit.Type))
                          .Build();
        }

        return await inner.HandleAsync(request);
    }

    private (string File, string Type)? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var dot = path.LastIndexOf('.');

        if (dot < 0 || !Types.TryGetValue(path[dot..], out var type))
            return null;

        var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        if (relative.Length == 0)
            return null;

        var full = Path.GetFullPath(Path.Combine(_root, relative));

        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return null;

        return (full, type);
    }
}
