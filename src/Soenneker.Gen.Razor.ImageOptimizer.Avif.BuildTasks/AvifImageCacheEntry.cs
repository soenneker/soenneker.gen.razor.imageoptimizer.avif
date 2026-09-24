using System.Collections.Generic;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

internal sealed record AvifImageCacheEntry(string Fingerprint, List<AvifImageVariant> Variants);
