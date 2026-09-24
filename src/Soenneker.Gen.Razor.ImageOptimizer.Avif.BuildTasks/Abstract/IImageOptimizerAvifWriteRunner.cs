using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;

/// <summary>
/// Runs the AVIF build-time optimizer from its command-line arguments.
/// </summary>
public interface IImageOptimizerAvifWriteRunner
{
    /// <summary>
    /// Discovers source images and writes full-size and responsive AVIF files without upscaling.
    /// The --widths argument accepts positive pixel widths separated by semicolons or commas
    /// (480;960;1440 by default); "none" disables resized variants. Unchanged sources and settings
    /// are skipped using the cache under obj/imageoptimizer-avif.
    /// Emits build warning AVIF001 when a requested width exceeds the source width, including on cached builds.
    /// </summary>
    /// <param name="args">Optimizer command-line arguments supplied by the MSBuild target.</param>
    /// <param name="cancellationToken">Cancels discovery or conversion.</param>
    /// <returns>Zero when the run succeeds; otherwise a nonzero process exit code.</returns>
    ValueTask<int> Run(string[] args, CancellationToken cancellationToken);
}
