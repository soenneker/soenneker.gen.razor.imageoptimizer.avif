using Soenneker.Utils.File.Abstract;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libvips.Util.Abstract;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.Tests;

public sealed class ImageOptimizerAvifTests
{
    [Test]
    public async Task Generates_real_variants_and_reuses_only_matching_inputs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ILibvipsUtil vips = provider.GetRequiredService<ILibvipsUtil>();
        IImageOptimizerAvifWriteRunner runner = provider.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            string images = Path.Combine(root, "wwwroot", "image folder");
            Directory.CreateDirectory(images);
            string source = Path.Combine(images, "sample.png");
            await vips.Run($"black \"{source}\" 1000 500 --bands 4", log: false);
            string[] args = ["--projectDir", root, "--widths", "1440;480;240;480;960", "--speed", "10", "--quality", "60"];
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            foreach (int width in new[] { 240, 480, 960, 1000 })
            {
                string file = Path.Combine(images, width == 1000 ? "sample.avif" : $"sample-{width}.avif");
                var actual = await vips.Identify(file);
                actual.Width.Should().Be(width);
                actual.Height.Should().Be(width / 2);
            }
            Directory.GetFiles(Path.Combine(root, "wwwroot"), "*.json", SearchOption.AllDirectories).Should().BeEmpty();
            (await provider.GetRequiredService<IFileUtil>().Exists(Path.Combine(images, "sample-1440.avif"))).Should().BeFalse();
            string output = Path.Combine(images, "sample-480.avif");
            DateTime original = (await provider.GetRequiredService<IFileUtil>().GetLastModified(output))!.Value.UtcDateTime;
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            (await provider.GetRequiredService<IFileUtil>().GetLastModified(output))!.Value.UtcDateTime.Should().Be(original);

            // Encoder changes must invalidate the cache even with identical source timestamps.
            args[^1] = "65";
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            (await provider.GetRequiredService<IFileUtil>().GetLastModified(output))!.Value.UtcDateTime.Should().BeAfter(original);
            await provider.GetRequiredService<IFileUtil>().Delete(output);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            (await provider.GetRequiredService<IFileUtil>().Exists(output)).Should().BeTrue();

            // Content hashes catch replacements even if a checkout preserves modification times.
            DateTime sourceTime = (await provider.GetRequiredService<IFileUtil>().GetLastModified(source))!.Value.UtcDateTime;
            await vips.Run($"black \"{source}\" 800 400 --bands 4", log: false);
            await provider.GetRequiredService<IFileUtil>().SetLastWriteTimeUtc(source, sourceTime);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            var smaller = await vips.Identify(Path.Combine(images, "sample.avif"));
            smaller.Width.Should().Be(800);
            smaller.Height.Should().Be(400);

            foreach (string variant in Directory.GetFiles(images, "sample-*.avif"))
                await provider.GetRequiredService<IFileUtil>().Delete(variant);
            (await runner.Run(["--projectDir", root, "--widths", "none", "--speed", "10"], CancellationToken.None)).Should().Be(0);
            Directory.GetFiles(images, "*.avif").Should().ContainSingle().Which.Should().Be(Path.Combine(images, "sample.avif"));
            Directory.GetFiles(images, ".*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    [Arguments(32, 16)]
    [Arguments(32, 64)]
    [Arguments(480, 960)]
    public async Task Small_images_and_separate_output_roots_generate_only_images(int width, int height)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var vips = provider.GetRequiredService<ILibvipsUtil>();
        var runner = provider.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            string image = Path.Combine(root, "wwwroot", "tiny.png");
            await vips.Run($"black \"{image}\" {width} {height} --bands 3", log: false);
            string[] args = ["--projectDir", root, "--outputPath", "generated", "--speed", "10"];
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            TestContext.Current!.Output.GetStandardOutput().Should().NotContain("warning AVIF001");
            (await runner.Run([..args, "--warnOnSkippedWidths", "true"], CancellationToken.None)).Should().Be(0);
            TestContext.Current!.Output.GetStandardOutput().Should().Contain("warning AVIF001");
            var actual = await vips.Identify(Path.Combine(root, "generated", "tiny.avif"));
            actual.Width.Should().Be(width);
            actual.Height.Should().Be(height);
            Directory.GetFiles(Path.Combine(root, "generated"), "*.json").Should().BeEmpty();
            Directory.GetFiles(Path.Combine(root, "generated"), "*.avif").Should().HaveCount(1);
            Directory.GetFiles(Path.Combine(root, "wwwroot"), "*.avif").Should().BeEmpty();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Portrait_variants_use_requested_width_and_preserve_aspect_ratio()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var vips = provider.GetRequiredService<ILibvipsUtil>();
        var runner = provider.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            string image = Path.Combine(root, "wwwroot", "portrait.png");
            await vips.Run($"black \"{image}\" 600 1200 --bands 3", log: false);
            (await runner.Run(["--projectDir", root, "--speed", "10"], CancellationToken.None)).Should().Be(0);
            var resized = await vips.Identify(Path.Combine(root, "wwwroot", "portrait-480.avif"));
            resized.Width.Should().Be(480);
            resized.Height.Should().Be(960);
            var full = await vips.Identify(Path.Combine(root, "wwwroot", "portrait.avif"));
            full.Width.Should().Be(600);
            full.Height.Should().Be(1200);
            Directory.GetFiles(Path.Combine(root, "wwwroot"), "*.avif").Should().HaveCount(2);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Invalid_widths_and_output_collisions_fail_before_writing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            foreach (string widths in new[] { "0", "-1", "480;abc", ";", "100001" })
                (await runner.Run(["--projectDir", root, "--widths", widths], CancellationToken.None)).Should().Be(1);
            await provider.GetRequiredService<IFileUtil>().Write(Path.Combine(root, "wwwroot", "photo.png"), "not decoded");
            await provider.GetRequiredService<IFileUtil>().Write(Path.Combine(root, "wwwroot", "photo-480.png"), "not decoded");
            (await runner.Run(["--projectDir", root], CancellationToken.None)).Should().Be(1);
            Directory.GetFiles(Path.Combine(root, "wwwroot"), "*.avif").Should().BeEmpty();
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "avif-generator-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        return root;
    }

    private static void DeleteRoot(string root)
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "avif-generator-tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        Directory.Delete(root, recursive: true);
    }
}
