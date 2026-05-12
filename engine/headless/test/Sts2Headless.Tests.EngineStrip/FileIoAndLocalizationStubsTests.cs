// Per-category inertia tests for Godot File-IO + Localization stubs.

using Godot;
using Sts2Headless.EngineStrip;
using FileAccess = Godot.FileAccess;

namespace Sts2Headless.Tests.EngineStrip;

[Collection("StubRegistry")]
public class ResourceLoaderStubsTests
{
    public ResourceLoaderStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Load_Texture2D_ReturnsSentinelAndRecords()
    {
        using var capture = StubRegistry.Capture();
        var tex = ResourceLoader.Load<Texture2D>("res://card.png", null, ResourceLoader.CacheMode.Reuse);
        Assert.NotNull(tex);
        Assert.Contains(capture.Hits, h => h.Type == nameof(ResourceLoader) && h.Member == nameof(ResourceLoader.Load));
        Assert.Contains(StubCategory.GodotFileIo, capture.Categories);
    }

    [Fact]
    public void Exists_AlwaysReturnsFalse()
    {
        using var capture = StubRegistry.Capture();
        Assert.False(ResourceLoader.Exists("res://anything.png"));
        Assert.Contains(capture.Hits, h => h.Member == nameof(ResourceLoader.Exists));
    }

    [Fact]
    public void CacheMode_EnumNamesUpstreamUses_AreDefined()
    {
        _ = ResourceLoader.CacheMode.Ignore;
        _ = ResourceLoader.CacheMode.Reuse;
        _ = ResourceLoader.CacheMode.Replace;
    }
}

[Collection("StubRegistry")]
public class ResourceSaverStubsTests
{
    public ResourceSaverStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Save_IsNoOp_ReturnsOk()
    {
        using var capture = StubRegistry.Capture();
        var result = ResourceSaver.Save(new Texture2D(), "res://out.tres");
        Assert.Equal(0, result);
        Assert.Contains(capture.Hits, h => h.Type == nameof(ResourceSaver) && h.Member == nameof(ResourceSaver.Save));
        Assert.Contains(StubCategory.GodotFileIo, capture.Categories);
    }
}

[Collection("StubRegistry")]
public class DirAccessStubsTests
{
    public DirAccessStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Open_ReturnsNull_AndRecords()
    {
        using var capture = StubRegistry.Capture();
        var dir = DirAccess.Open("res://scenes/backgrounds/foo/layers");
        Assert.Null(dir);
        Assert.Contains(capture.Hits, h => h.Type == nameof(DirAccess) && h.Member == nameof(DirAccess.Open));
    }
}

[Collection("StubRegistry")]
public class FileAccessStubsTests
{
    public FileAccessStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Open_ReturnsNull_AndRecords()
    {
        using var capture = StubRegistry.Capture();
        var f = FileAccess.Open("res://save.dat", FileAccess.ModeFlags.Read);
        Assert.Null(f);
        Assert.Contains(capture.Hits, h => h.Type == nameof(FileAccess) && h.Member == nameof(FileAccess.Open));
    }

    [Fact]
    public void FileExists_ReturnsFalse()
    {
        using var capture = StubRegistry.Capture();
        Assert.False(FileAccess.FileExists("res://save.dat"));
        Assert.Contains(capture.Hits, h => h.Member == nameof(FileAccess.FileExists));
    }
}

[Collection("StubRegistry")]
public class TranslationServerStubsTests
{
    public TranslationServerStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Translate_ReturnsKeyUnchanged()
    {
        using var capture = StubRegistry.Capture();
        Assert.Equal("ui.start", TranslationServer.Translate(new StringName("ui.start")));
        Assert.Equal("dlg.intro", TranslationServer.Translate(new StringName("dlg.intro"), new StringName("act1")));
        Assert.Contains(capture.Hits, h => h.Type == nameof(TranslationServer) && h.Member == nameof(TranslationServer.Translate));
        Assert.Contains(StubCategory.Localization, capture.Categories);
    }

    [Fact]
    public void Locale_RoundTrips()
    {
        using var capture = StubRegistry.Capture();
        TranslationServer.SetLocale("fr");
        Assert.Equal("fr", TranslationServer.GetLocale());
        Assert.Contains(capture.Hits, h => h.Member == nameof(TranslationServer.SetLocale));
        Assert.Contains(capture.Hits, h => h.Member == nameof(TranslationServer.GetLocale));
        // restore for other tests
        TranslationServer.SetLocale("en");
    }
}
