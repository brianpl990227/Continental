using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Continental.Launcher;

public static partial class Program
{
    private const string AppExe = "Continental.exe";
    private const string ReadyMark = ".ready";

    public static int Main(string[] args)
    {
        try
        {
            var self = Environment.ProcessPath ?? throw new InvalidOperationException("No se encuentra el propio ejecutable.");
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Continental");
            var payload = Payload.Read(self);
            var appDir = Path.Combine(root, $"app-{payload.Id}");

            if (!File.Exists(Path.Combine(appDir, ReadyMark)))
                Install(self, payload, root, appDir);

            Prune(root, appDir);
            Launch(appDir, root, args);

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, $"No se pudo abrir Continental.\n\n{ex.Message}", "Continental", 0x10);
            return 1;
        }
    }

    private static void Install(string self, Payload payload, string root, string appDir)
    {
        Directory.CreateDirectory(root);

        var staging = Path.Combine(root, $"staging-{Guid.NewGuid():N}");

        try
        {
            using (var file = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new Window(file, payload.Offset, payload.Length))
                ZipFile.ExtractToDirectory(zip, staging);

            File.WriteAllText(Path.Combine(staging, ReadyMark), payload.Id);

            if (Directory.Exists(appDir))
                Directory.Delete(appDir, recursive: true);

            Directory.Move(staging, appDir);
        }
        finally
        {
            if (Directory.Exists(staging))
                TryDelete(staging);
        }
    }

    private static void Prune(string root, string keep)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);

            if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.StartsWith("app-", StringComparison.Ordinal) || name.StartsWith("staging-", StringComparison.Ordinal))
                TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Launch(string appDir, string root, string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(appDir, AppExe))
        {
            UseShellExecute = false,
            WorkingDirectory = appDir
        };

        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        start.Environment["WEBVIEW2_USER_DATA_FOLDER"] = Path.Combine(root, "WebView2");

        Process.Start(start);
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);
}

public sealed record Payload(string Id, long Offset, long Length)
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("CONTINENTAL-PAYLOAD-1");

    public static Payload Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var trailer = Magic.Length + sizeof(long) + sizeof(int);

        if (file.Length < trailer)
            throw new InvalidDataException("El ejecutable no trae la aplicación dentro.");

        var tail = new byte[trailer];
        file.Seek(-trailer, SeekOrigin.End);
        file.ReadExactly(tail);

        if (!tail.AsSpan(sizeof(long) + sizeof(int)).SequenceEqual(Magic))
            throw new InvalidDataException("El ejecutable no trae la aplicación dentro.");

        var zipLength = BitConverter.ToInt64(tail, 0);
        var idLength = BitConverter.ToInt32(tail, sizeof(long));
        var idStart = file.Length - trailer - idLength;
        var zipStart = idStart - zipLength;

        if (idLength is <= 0 or > 128 || zipLength <= 0 || zipStart < 0)
            throw new InvalidDataException("El paquete de la aplicación está dañado.");

        var id = new byte[idLength];
        file.Seek(idStart, SeekOrigin.Begin);
        file.ReadExactly(id);

        return new Payload(Encoding.ASCII.GetString(id), zipStart, zipLength);
    }
}

public sealed class Window(Stream inner, long offset, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, length);
    }

    public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));

    public override int Read(Span<byte> buffer)
    {
        var left = length - _position;

        if (left <= 0)
            return 0;

        if (buffer.Length > left)
            buffer = buffer[..(int)left];

        inner.Position = offset + _position;
        var read = inner.Read(buffer);
        _position += read;

        return read;
    }

    public override long Seek(long delta, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => delta,
            SeekOrigin.Current => _position + delta,
            _ => length + delta
        };

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();
}
