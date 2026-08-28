namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

public sealed class BuildTasksCommandLineArgs
{
    public string[] Args { get; }

    public BuildTasksCommandLineArgs(string[] args)
    {
        Args = args;
    }
}
