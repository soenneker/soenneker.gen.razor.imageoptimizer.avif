using System.Collections.Generic;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

internal sealed class AvifImageManifest
{
    public int Version { get; init; } = 1;

    public Dictionary<string, List<AvifImageVariant>> Images { get; init; } = new();
}
