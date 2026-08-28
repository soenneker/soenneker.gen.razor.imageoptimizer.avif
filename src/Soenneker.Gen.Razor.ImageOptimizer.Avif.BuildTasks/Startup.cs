using Microsoft.Extensions.DependencyInjection;
using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Soenneker.Libavif.Util.Registrars;
using Soenneker.Libvips.Util.Registrars;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IImageOptimizerAvifWriteRunner, ImageOptimizerAvifWriteRunner>();
        services.AddLibavifUtilAsSingleton();
        services.AddLibvipsUtilAsSingleton();
        services.AddHostedService<ConsoleHostedService>();
    }
}
