using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Tests.Services.Tasks;

public sealed class StarterZipServiceTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "osc-starter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_RejectsMissingFile()
    {
        var result = await CreateService().SaveAsync(null);

        Assert.True(result.Failed);
        Assert.Equal("empty", result.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_RejectsNonZipExtension()
    {
        var result = await CreateService().SaveAsync(FormFile([1, 2, 3], "starter.rar"));

        Assert.True(result.Failed);
        Assert.Equal("not_zip", result.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidArchive()
    {
        var result = await CreateService().SaveAsync(FormFile("not a zip"u8.ToArray(), "starter.zip"));

        Assert.True(result.Failed);
        Assert.Equal("invalid_zip", result.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedArchive()
    {
        var result = await CreateService(maxTotalMb: 0).SaveAsync(FormFile(Zip("Program.cs"), "starter.zip"));

        Assert.True(result.Failed);
        Assert.Equal("too_large", result.ErrorCode);
    }

    [Fact]
    public async Task SaveAsync_StoresReadsAndDeletesArchive()
    {
        var service = CreateService();
        var bytes = Zip("Program.cs", "src/Tree.cs");

        var saved = await service.SaveAsync(FormFile(bytes, "Starter Project.zip"));

        Assert.False(saved.Failed);
        var upload = Assert.IsType<StarterZipUpload>(saved.Value);
        Assert.Equal("Starter Project.zip", upload.FileName);
        Assert.Equal(bytes.Length, upload.SizeBytes);
        Assert.StartsWith("starters/", upload.FilePath);
        Assert.True(File.Exists(Path.Combine(_contentRoot, "App_Data/submissions", upload.FilePath)));

        var read = service.Read(upload.FilePath, upload.FileName);
        Assert.NotNull(read);
        Assert.Equal(bytes.Length, read!.SizeBytes);

        service.Delete(upload.FilePath);

        Assert.Null(service.Read(upload.FilePath, upload.FileName));
        Assert.False(Directory.Exists(
            Path.Combine(_contentRoot, "App_Data/submissions", Path.GetDirectoryName(upload.FilePath)!)));
    }

    [Fact]
    public void Read_RejectsMissingPath()
    {
        Assert.Null(CreateService().Read(null, "starter.zip"));
        Assert.Null(CreateService().Read("../../etc/passwd", "starter.zip"));
    }

    private StarterZipService CreateService(int maxTotalMb = 30) =>
        new(
            new FakeEnvironment(_contentRoot),
            Options.Create(new UploadsOptions
            {
                Root = "App_Data/submissions",
                MaxTotalMb = maxTotalMb
            }),
            NullLogger<StarterZipService>.Instance);

    private static FormFile FormFile(byte[] bytes, string fileName) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "StarterZip", fileName);

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var file = archive.CreateEntry(entry);
                using var stream = file.Open();
                stream.WriteByte((byte)'/');
                stream.WriteByte((byte)'/');
            }
        }

        return buffer.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OscCommittee.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRoot;

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}