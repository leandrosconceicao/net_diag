using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using NetDiag.Core.Updates;

namespace NetDiag.Core.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdiag-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exe;

    private static readonly Uri ExeUrl = new("https://example.test/NetDiag.exe");
    private static readonly Uri ChecksumUrl = new("https://example.test/NetDiag.exe.sha256");

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "NetDiag.exe");
        File.WriteAllText(_exe, "versão antiga");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static UpdateInfo Update() =>
        new(new Version(0, 2, 0), "v0.2.0", "", new Uri("https://example.test/release"), ExeUrl, ChecksumUrl);

    /// <summary>Servidor falso: entrega o exe novo e o arquivo de checksum.</summary>
    private static HttpClient Server(byte[] exeBytes, string checksumText) =>
        new(new FakeHandler(req => req.RequestUri == ExeUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(exeBytes) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksumText) }));

    private sealed class SyncProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();
        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public async Task Matching_hash_installs_the_new_exe_and_keeps_the_old_one_aside()
    {
        var newExe = Encoding.UTF8.GetBytes("versão nova");
        var installer = new UpdateInstaller(Server(newExe, $"{Sha256Hex(newExe)}  NetDiag.exe"));

        await installer.InstallAsync(Update(), _exe, null, CancellationToken.None);

        Assert.Equal("versão nova", File.ReadAllText(_exe));
        Assert.Equal("versão antiga", File.ReadAllText(UpdateInstaller.OldPath(_exe)));
        Assert.False(File.Exists(UpdateInstaller.NewPath(_exe)));
    }

    [Fact]
    public async Task Wrong_hash_keeps_the_current_exe_untouched()
    {
        var newExe = Encoding.UTF8.GetBytes("versão nova");
        var otherHash = Sha256Hex(Encoding.UTF8.GetBytes("outra coisa"));
        var installer = new UpdateInstaller(Server(newExe, otherHash));

        await Assert.ThrowsAsync<UpdateException>(
            () => installer.InstallAsync(Update(), _exe, null, CancellationToken.None));

        Assert.Equal("versão antiga", File.ReadAllText(_exe));
        Assert.False(File.Exists(UpdateInstaller.NewPath(_exe)));
        Assert.False(File.Exists(UpdateInstaller.OldPath(_exe)));
    }

    [Fact]
    public async Task Invalid_checksum_file_aborts_before_downloading()
    {
        var installer = new UpdateInstaller(Server(Encoding.UTF8.GetBytes("x"), "<html>erro</html>"));

        await Assert.ThrowsAsync<UpdateException>(
            () => installer.InstallAsync(Update(), _exe, null, CancellationToken.None));

        Assert.Equal("versão antiga", File.ReadAllText(_exe));
        Assert.False(File.Exists(UpdateInstaller.NewPath(_exe)));
    }

    [Fact]
    public async Task Download_progress_reaches_100_percent()
    {
        var newExe = new byte[200_000];
        Random.Shared.NextBytes(newExe);
        var progress = new SyncProgress();

        await new UpdateInstaller(Server(newExe, Sha256Hex(newExe)))
            .InstallAsync(Update(), _exe, progress, CancellationToken.None);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(1.0, progress.Values[^1], 6);
    }

    [Fact]
    public void Swap_rolls_back_when_the_new_file_is_missing()
    {
        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(_exe, UpdateInstaller.NewPath(_exe)));

        Assert.Equal("versão antiga", File.ReadAllText(_exe));
        Assert.False(File.Exists(UpdateInstaller.OldPath(_exe)));
    }

    [Fact]
    public void Revert_restores_the_previous_version()
    {
        File.WriteAllText(UpdateInstaller.NewPath(_exe), "versão nova");
        UpdateInstaller.Swap(_exe, UpdateInstaller.NewPath(_exe));

        UpdateInstaller.Revert(_exe);

        Assert.Equal("versão antiga", File.ReadAllText(_exe));
        Assert.False(File.Exists(UpdateInstaller.OldPath(_exe)));
    }

    [Fact]
    public async Task Cleanup_removes_leftovers_of_a_previous_update()
    {
        File.WriteAllText(UpdateInstaller.OldPath(_exe), "sobra");
        File.WriteAllText(UpdateInstaller.NewPath(_exe), "download interrompido");

        await UpdateInstaller.CleanupOldVersionAsync(_exe, attempts: 1, delay: TimeSpan.Zero);

        Assert.False(File.Exists(UpdateInstaller.OldPath(_exe)));
        Assert.False(File.Exists(UpdateInstaller.NewPath(_exe)));
        Assert.True(File.Exists(_exe));
    }

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  NetDiag.exe")]
    [InlineData("  abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\r\n")]
    public void ParseChecksum_accepts_plain_and_sha256sum_formats(string text) =>
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            UpdateInstaller.ParseChecksum(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzz0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    public void ParseChecksum_rejects_invalid_content(string text) =>
        Assert.Throws<UpdateException>(() => UpdateInstaller.ParseChecksum(text));

    [Fact]
    public void CanWriteTo_detects_writable_and_missing_folders()
    {
        Assert.True(UpdateInstaller.CanWriteTo(_dir));
        Assert.False(UpdateInstaller.CanWriteTo(Path.Combine(_dir, "nao-existe")));
    }
}
