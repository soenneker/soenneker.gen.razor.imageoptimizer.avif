[![](https://img.shields.io/nuget/v/soenneker.gen.razor.imageoptimizer.avif.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.gen.razor.imageoptimizer.avif/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.gen.razor.imageoptimizer.avif/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.gen.razor.imageoptimizer.avif/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.gen.razor.imageoptimizer.avif.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.gen.razor.imageoptimizer.avif/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.gen.razor.imageoptimizer.avif/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.gen.razor.imageoptimizer.avif/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Gen.Razor.ImageOptimizer.Avif
### Build-time progressive AVIF image optimization for Razor applications using libvips and libavif.

## Install

```bash
dotnet add package Soenneker.Gen.Razor.ImageOptimizer.Avif
```

On each non-design-time build, the package scans `wwwroot` recursively for PNG and JPEG files, normalizes each source with libvips, and writes a sibling `.avif` file through libavif. Existing output is replaced only after a complete encode succeeds.

No application code or service registration is required.

## Configuration

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

`ImageOptimizerAvifOutputPath` may be absolute or relative to the project. When omitted, generated files are written beside their sources while preserving the directory tree. Outputs at least as new as their source are skipped unless `ImageOptimizerAvifForce` is `true`.

Set `ImageOptimizerAvifEnabled` to `false` for builds that must not generate assets. With `ImageOptimizerAvifFailOnError=true`, any image failure fails the build; with `false`, failures are reported and the remaining images continue. Cancellation always stops the build task.

`ImageOptimizerAvifSourceExtensions` must not include `avif`; the optimizer refuses to overwrite a discovered source. If two differently named source extensions would produce the same output name in one directory, the collision is reported rather than choosing one.

If `Soenneker.Gen.Razor.ImageOptimizer` is also installed, remove `avif` from its `ImageOptimizerFormats` setting so only this package owns AVIF output.
