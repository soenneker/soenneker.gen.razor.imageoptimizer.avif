[![](https://img.shields.io/nuget/v/soenneker.gen.razor.imageoptimizer.avif.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.gen.razor.imageoptimizer.avif/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.gen.razor.imageoptimizer.avif/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.gen.razor.imageoptimizer.avif/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.gen.razor.imageoptimizer.avif.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.gen.razor.imageoptimizer.avif/)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Gen.Razor.ImageOptimizer.Avif
### Build-time progressive AVIF image optimization for Razor applications using libvips and libavif.

## Installation

```
dotnet add package Soenneker.Gen.Razor.ImageOptimizer.Avif
```

The build target scans `wwwroot` for PNG and JPEG files, uses libvips to normalize each source, and uses the bundled libavif encoder to write adjacent `.avif` assets. Progressive layered encoding is enabled by default.

```xml
<PropertyGroup>
  <ImageOptimizerAvifEnabled>true</ImageOptimizerAvifEnabled>
  <ImageOptimizerAvifWwwRootPath>$(ProjectDir)wwwroot</ImageOptimizerAvifWwwRootPath>
  <ImageOptimizerAvifSourceExtensions>png;jpg;jpeg</ImageOptimizerAvifSourceExtensions>
  <ImageOptimizerAvifQuality>80</ImageOptimizerAvifQuality>
  <ImageOptimizerAvifSpeed>6</ImageOptimizerAvifSpeed>
  <ImageOptimizerAvifLossless>false</ImageOptimizerAvifLossless>
  <ImageOptimizerAvifProgressive>true</ImageOptimizerAvifProgressive>
  <ImageOptimizerAvifStripMetadata>true</ImageOptimizerAvifStripMetadata>
  <ImageOptimizerAvifForce>false</ImageOptimizerAvifForce>
  <ImageOptimizerAvifFailOnError>true</ImageOptimizerAvifFailOnError>
</PropertyGroup>
```

`ImageOptimizerAvifOutputPath` may be absolute or relative to the project. When omitted, generated files are written beside their sources. Outputs newer than their source are skipped unless `ImageOptimizerAvifForce` is `true`.

If `Soenneker.Gen.Razor.ImageOptimizer` is also installed, remove `avif` from its `ImageOptimizerFormats` setting so only this package owns AVIF output.
