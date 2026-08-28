using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks.Abstract;

public interface IImageOptimizerAvifWriteRunner
{
    ValueTask<int> Run(string[] args, CancellationToken cancellationToken);
}
