using Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

public sealed class Program
{
    private static CancellationTokenSource? _cts;

    public static async Task Main(string[] args)
    {
        _cts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddConsole());
            Startup.ConfigureServices(services);

            await using ServiceProvider provider = services.BuildServiceProvider();
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            Environment.ExitCode = await scope.ServiceProvider.GetRequiredService<IImageOptimizerAvifWriteRunner>().Run(args, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            Environment.ExitCode = 130;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Stopped program because of exception: {e}");
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _cts.Dispose();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(new BuildTasksCommandLineArgs(args));
                Startup.ConfigureServices(services);
            });
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        _cts?.Cancel();
    }
}
