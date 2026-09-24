using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libavif.Util.Abstract;
using Soenneker.Libavif.Util.Options;
using Soenneker.Libvips.Util.Abstract;
using Soenneker.Libvips.Util.Dtos;
using Soenneker.Libvips.Util.Options;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

public sealed class ImageOptimizerAvifWriteRunner : IImageOptimizerAvifWriteRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly ILibavifUtil _libavifUtil;
    private readonly ILibvipsUtil _libvipsUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;

    public ImageOptimizerAvifWriteRunner(ILibavifUtil libavifUtil, ILibvipsUtil libvipsUtil,
        IDirectoryUtil directoryUtil, IFileUtil fileUtil)
    {
        _libavifUtil = libavifUtil ?? throw new ArgumentNullException(nameof(libavifUtil));
        _libvipsUtil = libvipsUtil ?? throw new ArgumentNullException(nameof(libvipsUtil));
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
        bool warnOnSkippedWidths = ParseBoolean(GetOptional(map, "--warnOnSkippedWidths"), false);
        bool failOnError = ParseBoolean(GetOptional(map, "--failOnError"), true);
        string widthList = GetOptional(map, "--widths") ?? "480;960;1440";
        var widths = new SortedSet<int>();
        if (!widthList.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string value in ParseList(widthList))
            {
                if (!TryParseInt(value, 0, 1, 100_000, out int width))
                    return Fail("AVIF widths must be positive integers up to 100000, or 'none'");
                widths.Add(width);
            }

            if (widths.Count == 0)
                return Fail("AVIF widths must contain at least one width, or 'none'");
        }

        string cachePath = GetFullPath(GetOptional(map, "--cachePath") ?? "obj/imageoptimizer-avif/cache.json",
            projectDirectory!);

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

        return await Optimize(wwwRoot, outputRoot, sourceExtensions, options, widths, cachePath, force,
            failOnError, warnOnSkippedWidths, cancellationToken);
    }

    private async ValueTask<int> Optimize(string wwwRoot, string? outputRoot,
        IReadOnlyCollection<string> sourceExtensions, AvifEncodeOptions options, SortedSet<int> widths,
        string cachePath, bool force, bool failOnError, bool warnOnSkippedWidths,
        CancellationToken cancellationToken)
    {
        string destinationRoot = outputRoot ?? wwwRoot;
        var extensions = new HashSet<string>(sourceExtensions.Select(extension => "." + extension),
            StringComparer.OrdinalIgnoreCase);
        string[] sources =
            (await _fileUtil.GetAllFileNamesInDirectoryRecursively(wwwRoot, log: false, cancellationToken))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        // Reserve every possible output before writing anything. A source such as
        // photo-480.png must never be overwritten by a variant of photo.png.
        var claimedOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in sources)
        {
            string output = GetOutputPath(source, wwwRoot, outputRoot);
            foreach (string path in widths.Select(width => VariantPath(output, width)).Prepend(output))
            {
                if (sources.Contains(path, StringComparer.OrdinalIgnoreCase))
                    return Fail($"Refusing to overwrite source image '{path}'.");
                if (!claimedOutputs.TryAdd(path, source))
                    return Fail($"Output collision: '{source}' and '{claimedOutputs[path]}' both map to '{path}'.");
            }
        }

        Dictionary<string, AvifImageCacheEntry> previous = await ReadCache(cachePath, cancellationToken);
        var cache = new Dictionary<string, AvifImageCacheEntry>(StringComparer.OrdinalIgnoreCase);
        int generated = 0, skipped = 0, failed = 0;
        string settings = JsonSerializer.Serialize(options) + "|" + string.Join(",", widths) + "|" + destinationRoot +
                          "|" + typeof(ImageOptimizerAvifWriteRunner).Assembly.GetName().Version;

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string fingerprint;
                await using (FileStream stream = _fileUtil.OpenRead(source))
                    fingerprint = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)) + "|" +
                                  settings;

                if (!force && previous.TryGetValue(source, out AvifImageCacheEntry? entry) &&
                    entry.Fingerprint == fingerprint && entry.Variants.Count > 0 && await VariantsExist(destinationRoot, entry.Variants, cancellationToken))
                {
                    if (warnOnSkippedWidths)
                        WarnSkippedWidths(source, entry.Variants.Max(variant => variant.Width), widths);
                    cache[source] = entry;
                    skipped += entry.Variants.Count;
                    continue;
                }

                ImageInfo sourceInfo = await _libvipsUtil.Identify(source, cancellationToken);
                int sourceWidth = sourceInfo.Width;
                int sourceHeight = sourceInfo.Height;
                if (sourceInfo.TryGetMetadata("orientation", out string? orientation) &&
                    int.TryParse(orientation, out int value) && value is >= 5 and <= 8)
                    (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);
                if (sourceWidth <= 0 || sourceInfo.Height <= 0)
                    throw new InvalidDataException("The source image has invalid dimensions.");

                if (warnOnSkippedWidths)
                    WarnSkippedWidths(source, sourceWidth, widths);

                string output = GetOutputPath(source, wwwRoot, outputRoot);
                await _directoryUtil.Create(Path.GetDirectoryName(output)!, log: false, cancellationToken);
                var variants = new List<AvifImageVariant>();
                // Full-size remains the unsuffixed file; equal/larger widths are not duplicated.
                foreach (int width in widths.Where(width => width < sourceWidth).Append(sourceWidth))
                {
                    string path = width == sourceWidth ? output : VariantPath(output, width);
                    AvifImageVariant variant = await EncodeVariant(source, path, destinationRoot, width, sourceHeight,
                        width == sourceWidth, options, cancellationToken);
                    variants.Add(variant);
                    generated++;
                    Console.WriteLine(
                        $"Optimized {RelativePath(wwwRoot, source)} -> {path} ({variant.Width}x{variant.Height})");
                }

                cache[source] = new AvifImageCacheEntry(fingerprint, variants);
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
        }

        await WriteJsonIfChanged(cachePath, cache, cancellationToken);
        Console.WriteLine(
            $"Soenneker.Gen.Razor.ImageOptimizer.Avif: {sources.Length} source(s), {generated} generated, {skipped} up-to-date, {failed} failed.");
        return 0;
    }

    private async ValueTask<AvifImageVariant> EncodeVariant(string source, string output, string outputRoot, int width,
        int sourceHeight, bool fullSize, AvifEncodeOptions options, CancellationToken cancellationToken)
    {
        // PNG is a lossless intermediate; libavif still handles the progressive AVIF encode.
        // Normalize EXIF orientation even for the full-size output, before stripping metadata.
        string temporaryBase =
            Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid()}");
        string temporaryInput = temporaryBase + ".png";
        string temporaryOutput = temporaryBase + ".avif";
        try
        {
            if (fullSize)
            {
                await _libvipsUtil.AutoRotate(source, temporaryInput, new PngOptions { StripMetadata = false },
                    cancellationToken);
            }
            else
            {
                // An explicit height bound keeps portraits constrained by width instead of a square.
                await _libvipsUtil.Resize(source, temporaryInput, width, sourceHeight,
                    options: new PngOptions { StripMetadata = false }, cancellationToken: cancellationToken);
            }

            await _libavifUtil.Encode(temporaryInput, temporaryOutput, options, cancellationToken);
            ImageInfo encoded = await _libvipsUtil.Identify(temporaryOutput, cancellationToken);
            if (encoded.Width != width || encoded.Height <= 0)
                throw new InvalidDataException($"Expected width {width}, got {encoded.Width}x{encoded.Height}.");
            // Require a native atomic rename; FileUtil.Move can fall back to copy-and-delete.
            File.Move(temporaryOutput, output, overwrite: true);
            return new AvifImageVariant(RelativePath(outputRoot, output), encoded.Width, encoded.Height);
        }
        finally
        {
            await _fileUtil.TryDelete(temporaryInput, log: false, CancellationToken.None);
            await _fileUtil.TryDelete(temporaryOutput, log: false, CancellationToken.None);
        }
    }

    private static void WarnSkippedWidths(string source, int sourceWidth, IEnumerable<int> widths)
    {
        int[] skippedWidths = widths.Where(width => width > sourceWidth).ToArray();
        if (skippedWidths.Length == 0)
            return;

        // MSBuild Exec recognizes this format as a warning, including on cached builds.
        Console.WriteLine(
            $"{source}: warning AVIF001: Source image is {sourceWidth}px wide; skipped requested width(s) {string.Join(", ", skippedWidths)}px to avoid upscaling. Use a larger source image or reduce ImageOptimizerAvifWidths.");
    }

    private async ValueTask<bool> VariantsExist(string root, IEnumerable<AvifImageVariant> variants, CancellationToken cancellationToken)
    {
        foreach (AvifImageVariant variant in variants)
        {
            if (!await _fileUtil.Exists(Path.Combine(root, variant.Path), cancellationToken))
                return false;
        }

        return true;
    }

    private static string VariantPath(string output, int width) =>
        Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{width}.avif");

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private async Task<Dictionary<string, AvifImageCacheEntry>> ReadCache(string path,
        CancellationToken cancellationToken)
    {
        if ((await _fileUtil.Exists(path)))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, AvifImageCacheEntry>>(
                    await _fileUtil.Read(path, cancellationToken: cancellationToken), JsonOptions);
                if (entries is not null)
                    return new Dictionary<string, AvifImageCacheEntry>(entries, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                /* Rebuild an obsolete or interrupted cache. */
            }
        }

        return new Dictionary<string, AvifImageCacheEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteJsonIfChanged<T>(string path, T value, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        if ((await _fileUtil.Exists(path)) && await _fileUtil.Read(path, cancellationToken: cancellationToken) == json)
            return;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            await _fileUtil.Write(temporary, json, cancellationToken: cancellationToken);
            // Keep cache publication atomic; do not use a copy-and-delete move fallback.
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if ((await _fileUtil.Exists(temporary)))
                await _fileUtil.Delete(temporary);
        }
    }

    private static string GetOutputPath(string source, string wwwRoot, string? outputRoot)
    {
        string filename = Path.GetFileNameWithoutExtension(source) + ".avif";
        if (outputRoot is null)
            return Path.Combine(Path.GetDirectoryName(source)!, filename);

        string relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(wwwRoot, source)) ?? "";
        return Path.Combine(outputRoot, relativeDirectory, filename);
    }

    private static bool TryGetRequiredPath(IReadOnlyDictionary<string, string> map, string key, string? basePath,
        out string? path)
    {
        path = GetOptional(map, key);
        if (path is null)
            return false;
        path = GetFullPath(path, basePath ?? Environment.CurrentDirectory);
        return true;
    }

    private static string GetFullPath(string path, string basePath) =>
        Path.IsPathRooted(path.Trim().Trim('"'))
            ? Path.GetFullPath(path.Trim().Trim('"'))
            : Path.GetFullPath(Path.Combine(basePath, path.Trim().Trim('"')));

    private static string[] ParseList(string value) => value.Split([';', ','],
                                                                StringSplitOptions.RemoveEmptyEntries |
                                                                StringSplitOptions.TrimEntries)
                                                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TryParseInt(string? value, int defaultValue, int minimum, int maximum, out int result)
    {
        result = defaultValue;
        if (!string.IsNullOrWhiteSpace(value) && !int.TryParse(value.Trim().Trim('"'), out result))
            return false;
        return result >= minimum && result <= maximum;
    }

    private static bool ParseBoolean(string? value, bool defaultValue) => string.IsNullOrWhiteSpace(value)
        ?
        defaultValue
        : bool.TryParse(value.Trim().Trim('"'), out bool result)
            ? result
            : defaultValue;

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
