using Microsoft.Extensions.DependencyInjection;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libavif.Util.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Registrars;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IImageOptimizerAvifWriteRunner, ImageOptimizerAvifWriteRunner>();
        services.AddDirectoryUtilAsSingleton();
        services.AddFileUtilAsSingleton();
        services.AddLibavifUtilAsSingleton();
        services.AddHostedService<ConsoleHostedService>();
    }
}
