namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

// Paths are relative to the output root; widths describe actual encoded pixels.
internal sealed record AvifImageVariant(string Path, int Width, int Height);
