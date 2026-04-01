using FluentAssertions;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Tests.Unit;

public sealed class MissionPlanStoreTests : IDisposable
{
    private readonly string _tempDir;

    public MissionPlanStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private MissionPlanStore CreateStore() =>
        new(Path.Combine(_tempDir, "mission.plan"));

    [Fact]
    public void Save_WritesFileToDisk_ReturnsTrue()
    {
        var store = CreateStore();

        var result = store.Save("""{"fileType":"Plan"}""");

        result.Should().BeTrue();
    }

    [Fact]
    public void Save_WhenPathIsReadOnly_ReturnsFalse()
    {
        // Use an invalid path (directory as the file path) to force IOException
        var store = new MissionPlanStore(_tempDir); // _tempDir itself is a directory

        var result = store.Save("""{"fileType":"Plan"}""");

        result.Should().BeFalse();
    }

    [Fact]
    public void Load_AfterSave_ReturnsSavedJson()
    {
        var store = CreateStore();
        const string json = """{"fileType":"Plan","version":1}""";
        store.Save(json);

        var loaded = store.Load();

        loaded.Should().Be(json);
    }

    [Fact]
    public void Load_WhenNoFileExists_ReturnsNull()
    {
        var store = CreateStore();

        var loaded = store.Load();

        loaded.Should().BeNull();
    }

    [Fact]
    public void Clear_WhenFileExists_DeletesFileAndReturnsTrue()
    {
        var store = CreateStore();
        store.Save("""{"fileType":"Plan"}""");

        var result = store.Clear();

        result.Should().BeTrue();
        store.Load().Should().BeNull();
    }

    [Fact]
    public void Clear_WhenNoFileExists_ReturnsTrue()
    {
        var store = CreateStore();

        var result = store.Clear();

        result.Should().BeTrue();
    }
}
