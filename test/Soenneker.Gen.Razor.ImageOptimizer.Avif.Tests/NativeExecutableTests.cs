using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;
using System.Threading;
namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.Tests;

public sealed class NativeExecutableTests
{
    [Test]
    public async Task Native_executable_generates_expected_output(CancellationToken cancellationToken)
    {
        string? executable = Environment.GetEnvironmentVariable("RAZOR_NATIVE_TOOL");
        if (string.IsNullOrEmpty(executable)) { TUnit.Core.Skip.Test("Set RAZOR_NATIVE_TOOL to run native integration tests."); return; }
        string directory = Path.Combine(Path.GetTempPath(), "razor native " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            async Task Run(string[] arguments)
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new Exception("Native tool could not start.");
                await process.WaitForExitAsync(cancellationToken: cancellationToken);
                if (process.ExitCode != 0) throw new Exception("Native tool failed: " + process.ExitCode);
            }
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging();
            Startup.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            var vips = provider.GetRequiredService<Soenneker.Libvips.Util.Abstract.ILibvipsUtil>();
            string images = Path.Combine(directory, "wwwroot", "image folder");
            Directory.CreateDirectory(images);
            string source = Path.Combine(images, "sample.png");
            await vips.Run($"black \"{source}\" 100 50 --bands 3", log: false, cancellationToken: cancellationToken);
            string[] arguments = ["--projectDir", directory, "--widths", "none", "--speed", "10"];
            await Run(arguments);
            string output = Path.Combine(images, "sample.avif");
            var info = await vips.Identify(output, cancellationToken: cancellationToken);
            if (info.Width != 100 || info.Height != 50) throw new Exception("Native image dimensions are incorrect.");
            DateTime timestamp = File.GetLastWriteTimeUtc(output);
            await Run(arguments);
            if (File.GetLastWriteTimeUtc(output) != timestamp) throw new Exception("Native image cache did not preserve unchanged output.");
            File.Delete(output);
            await Run(arguments);
            if (!File.Exists(output)) throw new Exception("Native tool did not recreate a missing image.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
