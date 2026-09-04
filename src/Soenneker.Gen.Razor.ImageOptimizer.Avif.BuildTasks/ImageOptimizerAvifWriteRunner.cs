using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libavif.Util.Abstract;
using Soenneker.Libavif.Util.Options;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

/// <inheritdoc cref="IImageOptimizerAvifWriteRunner" />
public sealed class ImageOptimizerAvifWriteRunner : IImageOptimizerAvifWriteRunner
{
    private readonly ILibavifUtil _libavifUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;

    public ImageOptimizerAvifWriteRunner(ILibavifUtil libavifUtil, IDirectoryUtil directoryUtil, IFileUtil fileUtil)
    {
        _libavifUtil = libavifUtil ?? throw new ArgumentNullException(nameof(libavifUtil));
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
    }

    public async ValueTask<int> Run(string[] args, CancellationToken cancellationToken)
    {
        Dictionary<string, string> map = ParseArgs(args);
        if (!TryGetRequiredPath(map, "--projectDir", null, out string? projectDirectory))
            return Fail("Missing required --projectDir");

        string wwwRoot = GetFullPath(GetOptional(map, "--wwwRoot") ?? "wwwroot", projectDirectory!);
        string? outputRoot = GetOptional(map, "--outputPath");
        if (outputRoot is not null)
            outputRoot = GetFullPath(outputRoot, projectDirectory!);

        string[] sourceExtensions = ParseList(GetOptional(map, "--sourceExtensions") ?? "png;jpg;jpeg")
                                    .Select(extension => extension.TrimStart('.')).ToArray();

        if (!TryParseInt(GetOptional(map, "--quality"), 80, 0, 100, out int quality))
            return Fail("AVIF quality must be between 0 and 100");
        if (!TryParseInt(GetOptional(map, "--speed"), 6, 0, 10, out int speed))
            return Fail("AVIF speed must be between 0 and 10");

        bool lossless = ParseBoolean(GetOptional(map, "--lossless"), false);
        bool progressive = ParseBoolean(GetOptional(map, "--progressive"), true);
        bool stripMetadata = ParseBoolean(GetOptional(map, "--stripMetadata"), true);
        bool force = ParseBoolean(GetOptional(map, "--force"), false);
        bool failOnError = ParseBoolean(GetOptional(map, "--failOnError"), true);

        if (!await _directoryUtil.Exists(wwwRoot, cancellationToken))
        {
            Console.WriteLine($"Soenneker.Gen.Razor.ImageOptimizer.Avif: wwwroot not found; skipping '{wwwRoot}'.");
            return 0;
        }

        var options = new AvifEncodeOptions
        {
            Quality = quality,
            Speed = speed,
            Lossless = lossless,
            Progressive = progressive,
            StripMetadata = stripMetadata
        };

        return await Optimize(wwwRoot, outputRoot, sourceExtensions, options, force, failOnError, cancellationToken);
    }

    private async ValueTask<int> Optimize(string wwwRoot, string? outputRoot, IReadOnlyCollection<string> sourceExtensions,
        AvifEncodeOptions options, bool force, bool failOnError, CancellationToken cancellationToken)
    {
        var extensions = new HashSet<string>(sourceExtensions.Select(extension => "." + extension), StringComparer.OrdinalIgnoreCase);
        string[] sources = (await _fileUtil.GetAllFileNamesInDirectoryRecursively(wwwRoot, log: false, cancellationToken))
                           .Where(path => extensions.Contains(Path.GetExtension(path)))
                           .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        var generated = 0;
        var skipped = 0;
        var failed = 0;
        var claimedOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string output = GetOutputPath(source, wwwRoot, outputRoot);

            if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
            {
                failed++;
                await Console.Error.WriteLineAsync(
                    $"Refusing to overwrite source image '{source}'. Remove 'avif' from ImageOptimizerAvifSourceExtensions.");
                if (failOnError)
                    return 1;
                continue;
            }

            if (claimedOutputs.TryGetValue(output, out string? claimedBy) &&
                !string.Equals(source, claimedBy, StringComparison.OrdinalIgnoreCase))
            {
                failed++;
                await Console.Error.WriteLineAsync($"Output collision: '{source}' and '{claimedBy}' both map to '{output}'.");
                if (failOnError)
                    return 1;
                continue;
            }

            claimedOutputs[output] = source;
            if (!force && await IsUpToDate(source, output, cancellationToken))
            {
                skipped++;
                continue;
            }

            string outputDirectory = Path.GetDirectoryName(output)!;
            await _directoryUtil.Create(outputDirectory, log: false, cancellationToken);
            string temporaryOutput = Path.Combine(outputDirectory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp.avif");

            try
            {
                await _libavifUtil.Encode(source, temporaryOutput, options, cancellationToken);

                await _fileUtil.Move(temporaryOutput, output, log: false, cancellationToken);
                generated++;
                Console.WriteLine($"Optimized {Path.GetRelativePath(wwwRoot, source)} -> {output}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                await Console.Error.WriteLineAsync($"Failed to optimize '{source}' as AVIF: {exception.Message}");
                if (failOnError)
                    return 1;
            }
            finally
            {
                await _fileUtil.TryDelete(temporaryOutput, log: false, CancellationToken.None);
            }
        }

        Console.WriteLine($"Soenneker.Gen.Razor.ImageOptimizer.Avif: {sources.Length} source(s), {generated} generated, {skipped} up-to-date, {failed} failed.");
        return failed > 0 && failOnError ? 1 : 0;
    }

    private async ValueTask<bool> IsUpToDate(string source, string output, CancellationToken cancellationToken)
    {
        DateTimeOffset? outputModified = await _fileUtil.GetLastModified(output, cancellationToken);
        if (outputModified is null)
            return false;

        DateTimeOffset? sourceModified = await _fileUtil.GetLastModified(source, cancellationToken);
        return sourceModified is not null && outputModified >= sourceModified;
    }

    private static string GetOutputPath(string source, string wwwRoot, string? outputRoot)
    {
        string filename = Path.GetFileNameWithoutExtension(source) + ".avif";
        if (outputRoot is null)
            return Path.Combine(Path.GetDirectoryName(source)!, filename);

        string relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(wwwRoot, source)) ?? "";
        return Path.Combine(outputRoot, relativeDirectory, filename);
    }

    private static bool TryGetRequiredPath(IReadOnlyDictionary<string, string> map, string key, string? basePath, out string? path)
    {
        path = GetOptional(map, key);
        if (path is null)
            return false;
        path = GetFullPath(path, basePath ?? Environment.CurrentDirectory);
        return true;
    }

    private static string GetFullPath(string path, string basePath) =>
        Path.IsPathRooted(path.Trim().Trim('"')) ? Path.GetFullPath(path.Trim().Trim('"')) :
            Path.GetFullPath(Path.Combine(basePath, path.Trim().Trim('"')));

    private static string[] ParseList(string value) => value.Split([';', ','],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TryParseInt(string? value, int defaultValue, int minimum, int maximum, out int result)
    {
        result = defaultValue;
        if (!string.IsNullOrWhiteSpace(value) && !int.TryParse(value.Trim().Trim('"'), out result))
            return false;
        return result >= minimum && result <= maximum;
    }

    private static bool ParseBoolean(string? value, bool defaultValue) => string.IsNullOrWhiteSpace(value) ? defaultValue :
        bool.TryParse(value.Trim().Trim('"'), out bool result) ? result : defaultValue;

    private static string? GetOptional(IReadOnlyDictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim().Trim('"') : null;

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length)
                map[args[index]] = args[++index];
        }
        return map;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Soenneker.Gen.Razor.ImageOptimizer.Avif: {message}");
        return 1;
    }
}
