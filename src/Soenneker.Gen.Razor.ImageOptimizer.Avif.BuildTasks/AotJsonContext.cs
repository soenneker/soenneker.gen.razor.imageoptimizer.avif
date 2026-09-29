using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, AvifImageCacheEntry>))]
[JsonSerializable(typeof(Soenneker.Libavif.Util.Options.AvifEncodeOptions))]
internal partial class AotJsonContext : JsonSerializerContext
{
    internal static JsonTypeInfo<T> Get<T>(JsonSerializerOptions? options = null) =>
        (JsonTypeInfo<T>)((options is null ? Default : new AotJsonContext(new JsonSerializerOptions(options))).GetTypeInfo(typeof(T))
            ?? throw new NotSupportedException($"No generated JSON metadata for {typeof(T)}."));
}
