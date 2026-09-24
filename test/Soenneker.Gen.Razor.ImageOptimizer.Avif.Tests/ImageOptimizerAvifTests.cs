using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libvips.Util.Abstract;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.Tests;

public sealed class ImageOptimizerAvifTests
{
    [Test]
    public async Task Generates_real_variants_and_reuses_only_matching_inputs()
    {
        using IHost host = Program.CreateHostBuilder([]).Build();
        ILibvipsUtil vips = host.Services.GetRequiredService<ILibvipsUtil>();
        IImageOptimizerAvifWriteRunner runner = host.Services.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            string images = Path.Combine(root, "wwwroot", "image folder");
            Directory.CreateDirectory(images);
            string source = Path.Combine(images, "sample.png");
            await vips.Run($"black \"{source}\" 1000 500 --bands 4", log: false);
            string[] args = ["--projectDir", root, "--widths", "1440;480;240;480;960", "--speed", "10", "--quality", "60"];
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            string manifestPath = Path.Combine(root, "wwwroot", "image-variants.json");
            using (JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)))
            {
                JsonElement variants = manifest.RootElement.GetProperty("images").GetProperty("image folder/sample.png");
                variants.EnumerateArray().Select(v => v.GetProperty("width").GetInt32()).Should().Equal(240, 480, 960, 1000);
                foreach (JsonElement variant in variants.EnumerateArray())
                {
                    string file = Path.Combine(root, "wwwroot", variant.GetProperty("path").GetString()!);
                    var actual = await vips.Identify(file);
                    actual.Width.Should().Be(variant.GetProperty("width").GetInt32());
                    actual.Height.Should().Be(variant.GetProperty("height").GetInt32());
                    actual.Height.Should().Be(actual.Width / 2);
                }
            }
            File.Exists(Path.Combine(images, "sample-1440.avif")).Should().BeFalse();
            string output = Path.Combine(images, "sample-480.avif");
            DateTime original = File.GetLastWriteTimeUtc(output);
            DateTime manifestTime = File.GetLastWriteTimeUtc(manifestPath);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            File.GetLastWriteTimeUtc(output).Should().Be(original);
            File.GetLastWriteTimeUtc(manifestPath).Should().Be(manifestTime);

            // Encoder changes must invalidate the cache even with identical source timestamps.
            args[^1] = "65";
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            File.GetLastWriteTimeUtc(output).Should().BeAfter(original);
            File.Delete(output);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            File.Exists(output).Should().BeTrue();

            // Content hashes catch replacements even if a checkout preserves modification times.
            DateTime sourceTime = File.GetLastWriteTimeUtc(source);
            await vips.Run($"black \"{source}\" 800 400 --bands 4", log: false);
            File.SetLastWriteTimeUtc(source, sourceTime);
            (await runner.Run(args, CancellationToken.None)).Should().Be(0);
            using (JsonDocument smaller = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)))
                smaller.RootElement.GetProperty("images").GetProperty("image folder/sample.png")
                    .EnumerateArray().Select(v => v.GetProperty("width").GetInt32()).Should().Equal(240, 480, 800);

            (await runner.Run(["--projectDir", root, "--widths", "none", "--speed", "10"], CancellationToken.None)).Should().Be(0);
            using JsonDocument fullOnly = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            fullOnly.RootElement.GetProperty("images").GetProperty("image folder/sample.png").GetArrayLength().Should().Be(1);
            Directory.GetFiles(images, ".*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    [Arguments(32, 16)]
    [Arguments(32, 64)]
    [Arguments(480, 960)]
    public async Task Small_images_and_separate_output_roots_have_truthful_manifest_paths(int width, int height)
    {
        using IHost host = Program.CreateHostBuilder([]).Build();
        var vips = host.Services.GetRequiredService<ILibvipsUtil>();
        var runner = host.Services.GetRequiredService<IImageOptimizerAvifWriteRunner>();
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
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "generated", "image-variants.json")));
            JsonElement variants = manifest.RootElement.GetProperty("images").GetProperty("tiny.png");
            variants.GetArrayLength().Should().Be(1);
            variants[0].GetProperty("path").GetString().Should().Be("tiny.avif");
            variants[0].GetProperty("width").GetInt32().Should().Be(width);
            variants[0].GetProperty("height").GetInt32().Should().Be(height);
            Directory.GetFiles(Path.Combine(root, "generated"), "*.avif").Should().HaveCount(1);
            Directory.GetFiles(Path.Combine(root, "wwwroot"), "*.avif").Should().BeEmpty();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Portrait_variants_use_requested_width_and_preserve_aspect_ratio()
    {
        using IHost host = Program.CreateHostBuilder([]).Build();
        var vips = host.Services.GetRequiredService<ILibvipsUtil>();
        var runner = host.Services.GetRequiredService<IImageOptimizerAvifWriteRunner>();
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
        using IHost host = Program.CreateHostBuilder([]).Build();
        var runner = host.Services.GetRequiredService<IImageOptimizerAvifWriteRunner>();
        string root = CreateRoot();
        try
        {
            foreach (string widths in new[] { "0", "-1", "480;abc", ";", "100001" })
                (await runner.Run(["--projectDir", root, "--widths", widths], CancellationToken.None)).Should().Be(1);
            await File.WriteAllTextAsync(Path.Combine(root, "wwwroot", "photo.png"), "not decoded");
            await File.WriteAllTextAsync(Path.Combine(root, "wwwroot", "photo-480.png"), "not decoded");
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
