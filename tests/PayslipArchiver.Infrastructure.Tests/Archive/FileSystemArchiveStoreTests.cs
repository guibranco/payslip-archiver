using Microsoft.Extensions.Options;

using PayslipArchiver.Infrastructure.Archive;

namespace PayslipArchiver.Infrastructure.Tests.Archive;

public sealed class FileSystemArchiveStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("payslip-archive-tests-").FullName;
    private readonly FileSystemArchiveStore _sut;

    public FileSystemArchiveStoreTests()
    {
        _sut = new FileSystemArchiveStore(Options.Create(new ArchiveOptions { Provider = ArchiveProvider.FileSystem, Directory = _root }));
    }

    [Fact]
    public async Task Store_WritesTheFile_CreatingDirectories()
    {
        var path = await _sut.StoreAsync("/2026/2026-10 Payslip.pdf", "hello"u8.ToArray(), CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "2026", "2026-10 Payslip.pdf"), path);
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Store_NeverOverwrites_ItRenamesInstead()
    {
        var first = await _sut.StoreAsync("/2026/2026-10 Payslip.pdf", "one"u8.ToArray(), CancellationToken.None);
        var second = await _sut.StoreAsync("/2026/2026-10 Payslip.pdf", "two"u8.ToArray(), CancellationToken.None);
        var third = await _sut.StoreAsync("/2026/2026-10 Payslip.pdf", "three"u8.ToArray(), CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "2026", "2026-10 Payslip.pdf"), first);
        Assert.Equal(Path.Combine(_root, "2026", "2026-10 Payslip (1).pdf"), second);
        Assert.Equal(Path.Combine(_root, "2026", "2026-10 Payslip (2).pdf"), third);
        Assert.Equal("one", await File.ReadAllTextAsync(first));
        Assert.Equal("two", await File.ReadAllTextAsync(second));
        Assert.Equal("three", await File.ReadAllTextAsync(third));
    }

    [Theory]
    [InlineData("/../outside.pdf")]
    [InlineData("/2026/../../outside.pdf")]
    public async Task Store_RefusesToEscapeTheRoot(string path)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.StoreAsync(path, "x"u8.ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task Store_RefusesASiblingDirectoryThatSharesTheRootPrefix()
    {
        var sibling = _root + "-other";
        var escaping = "/../" + Path.GetFileName(sibling) + "/x.pdf";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.StoreAsync(escaping, "x"u8.ToArray(), CancellationToken.None));
        Assert.False(Directory.Exists(sibling));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

public sealed class DropboxArchiveStorePathTests
{
    [Theory]
    [InlineData("/", "/2026/a.pdf", "/2026/a.pdf")]
    [InlineData("", "2026/a.pdf", "/2026/a.pdf")]
    [InlineData("/Documents/Payslips", "/2026/a.pdf", "/Documents/Payslips/2026/a.pdf")]
    [InlineData("/Documents/Payslips/", "2026/a.pdf", "/Documents/Payslips/2026/a.pdf")]
    [InlineData("Documents\\Payslips", "\\2026\\a.pdf", "/Documents/Payslips/2026/a.pdf")]
    public void CombinePaths_ProducesADropboxPath(string root, string path, string expected)
    {
        Assert.Equal(expected, DropboxArchiveStore.CombinePaths(root, path));
    }
}