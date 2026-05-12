// Godot File-IO category — M8 stubs.
//
// Upstream Godot surfaces covered (sampled from `src/Core/Models/`):
//   * Godot.ResourceLoader (static) — Load<T>(path, type, cacheMode), Exists(path), CacheMode enum.
//                                     Heavily used by CardModel/PowerModel/RelicModel for
//                                     Texture2D-typed icon/portrait paths.
//   * Godot.ResourceLoader.CacheMode (enum) — Ignore / Reuse / Replace; only the names
//                                              upstream references need to exist.
//   * Godot.ResourceSaver (static) — Save(resource, path, flags); included for parity
//                                    even if Phase-1 model code doesn't write.
//   * Godot.DirAccess — Open(path), GetFiles(); used by ActModel.GetAllBackgroundLayerPaths
//                       (Phase-2). IDisposable.
//   * Godot.FileAccess — placeholder for Phase-2 / save-system code paths; throws via
//                        ThrowNotStubbed on any method until S6+ adds coverage.
//
// All members default-return; the spec is explicit: "Replaced with .NET System.IO
// equivalents inside M3 (replay) / M9 (config); domain code does not access files."
// These stubs return empty/null so model property getters degrade safely.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's static <c>ResourceLoader</c>. <see cref="Load{T}"/> returns
/// a default <typeparamref name="T"/> sentinel (<c>new T()</c> for <see cref="Texture2D"/>,
/// <c>null</c> for other reference types via <see cref="Activator"/>). <see cref="Exists"/>
/// always returns false — there is no resource pack.
/// </summary>
public static class ResourceLoader
{
    public enum CacheMode
    {
        Ignore,
        Reuse,
        Replace,
        IgnoreDeep,
        ReplaceDeep,
    }

    /// <summary>
    /// Stub for <c>ResourceLoader.Load&lt;T&gt;(path, typeHint, cacheMode)</c>. Returns a
    /// fresh default instance of <typeparamref name="T"/> when <typeparamref name="T"/>
    /// has a public parameterless constructor (e.g., <see cref="Texture2D"/>), otherwise
    /// <c>default</c>. Records the path so tests can assert which resources were requested.
    /// </summary>
    public static T? Load<T>(string path, string? typeHint = null, CacheMode cacheMode = CacheMode.Reuse) where T : class
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(ResourceLoader),
            nameof(Load),
            $"T={typeof(T).Name},path={path}");
        try
        {
            return Activator.CreateInstance<T>();
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Always returns false — headless has no resource pack.</summary>
    public static bool Exists(string path, string? typeHint = null)
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(ResourceLoader),
            nameof(Exists),
            $"path={path}");
        return false;
    }
}

/// <summary>
/// Headless stub for Godot's static <c>ResourceSaver</c>. No-op; the spec says resource
/// writes go through .NET System.IO inside M3/M9, not Godot's resource layer.
/// </summary>
public static class ResourceSaver
{
    [Flags]
    public enum SaverFlags
    {
        None = 0,
        RelativePaths = 1,
        Bundle = 2,
        ChangePath = 4,
        OmitEditorProperties = 8,
        SaveBigEndian = 16,
        Compress = 32,
        ReplaceSubresourcePaths = 64,
    }

    /// <summary>No-op save. Returns <c>Error.Ok</c> sentinel.</summary>
    public static int Save(object? resource, string path = "", SaverFlags flags = SaverFlags.None)
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(ResourceSaver),
            nameof(Save),
            $"path={path}");
        return 0; // Godot.Error.Ok
    }
}

/// <summary>
/// Headless stub for Godot's <c>DirAccess</c>. <see cref="Open"/> returns
/// <c>null</c> (no directory) — upstream callers (e.g., <c>ActModel</c>) already handle
/// the null case. The class is <see cref="IDisposable"/> to match the <c>using</c>
/// pattern in upstream code.
/// </summary>
public class DirAccess : IDisposable
{
    private bool _disposed;

    private DirAccess()
    {
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(DirAccess), ".ctor");
    }

    public static DirAccess? Open(string path)
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(DirAccess),
            nameof(Open),
            $"path={path}");
        // Headless has no filesystem under res://; null is the documented "missing" return.
        return null;
    }

    /// <summary>Empty file list — there is no directory under res:// in headless.</summary>
    public string[] GetFiles()
    {
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(DirAccess), nameof(GetFiles));
        return Array.Empty<string>();
    }

    public string[] GetDirectories()
    {
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(DirAccess), nameof(GetDirectories));
        return Array.Empty<string>();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(DirAccess), nameof(Dispose));
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Headless stub for Godot's <c>FileAccess</c>. Inert: <see cref="Open"/> returns null
/// so upstream callers fall through to the missing-file branch. Methods throw via
/// <see cref="StubRegistry.ThrowNotStubbed"/> to flag any new Phase-2 surface; this is
/// the R4 mitigation — naming the unstubbed surface points the next stage's agent at it.
/// </summary>
public class FileAccess : IDisposable
{
    public enum ModeFlags
    {
        Read = 1,
        Write = 2,
        ReadWrite = 3,
        WriteRead = 7,
    }

    private bool _disposed;

    private FileAccess()
    {
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(FileAccess), ".ctor");
    }

    public static FileAccess? Open(string path, ModeFlags flags)
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(FileAccess),
            nameof(Open),
            $"path={path},flags={flags}");
        return null;
    }

    public static bool FileExists(string path)
    {
        StubRegistry.Record(
            StubCategory.GodotFileIo,
            nameof(FileAccess),
            nameof(FileExists),
            $"path={path}");
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        StubRegistry.Record(StubCategory.GodotFileIo, nameof(FileAccess), nameof(Dispose));
        GC.SuppressFinalize(this);
    }
}
