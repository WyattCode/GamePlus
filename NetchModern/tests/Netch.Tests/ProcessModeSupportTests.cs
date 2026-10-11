using Netch.Core.Services;

namespace Netch.Tests;

public sealed class ProcessModeSupportTests : IDisposable
{
    private readonly string _baseDirectory;

    public ProcessModeSupportTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"netch-pm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDirectory, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void AllFilesMissing_IsNotAvailable_AndListsAllRequiredFiles()
    {
        var missing = ProcessModeSupport.MissingFiles(_baseDirectory);

        Assert.False(ProcessModeSupport.IsAvailable(_baseDirectory));
        Assert.Equal(ProcessModeSupport.RequiredFiles.OrderBy(f => f), missing.OrderBy(f => f));
        Assert.Equal(3, missing.Count);
    }

    [Fact]
    public void FilesInBinSubfolder_IsAvailable()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_baseDirectory, "bin"));
        foreach (var file in ProcessModeSupport.RequiredFiles)
            File.WriteAllText(Path.Combine(bin.FullName, file), string.Empty);

        Assert.True(ProcessModeSupport.IsAvailable(_baseDirectory));
        Assert.Empty(ProcessModeSupport.MissingFiles(_baseDirectory));
    }

    [Fact]
    public void FilesInBaseDirectoryRoot_IsAvailable()
    {
        foreach (var file in ProcessModeSupport.RequiredFiles)
            File.WriteAllText(Path.Combine(_baseDirectory, file), string.Empty);

        Assert.True(ProcessModeSupport.IsAvailable(_baseDirectory));
        Assert.Empty(ProcessModeSupport.MissingFiles(_baseDirectory));
    }

    [Fact]
    public void OneFileMissing_MissingFilesReturnsExactlyThatFile()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_baseDirectory, "bin"));
        foreach (var file in ProcessModeSupport.RequiredFiles.Skip(1))
            File.WriteAllText(Path.Combine(bin.FullName, file), string.Empty);

        var missing = ProcessModeSupport.MissingFiles(_baseDirectory);

        Assert.False(ProcessModeSupport.IsAvailable(_baseDirectory));
        var file0 = ProcessModeSupport.RequiredFiles[0];
        Assert.Equal(new[] { file0 }, missing);
    }
}
