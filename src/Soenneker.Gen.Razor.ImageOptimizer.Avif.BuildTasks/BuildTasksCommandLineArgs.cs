namespace Soenneker.Gen.Razor.ImageOptimizer.Avif.BuildTasks;

/// <summary>
/// Holds the arguments passed to the build-task executable.
/// </summary>
public sealed class BuildTasksCommandLineArgs
{
    /// <summary>
    /// Gets the unmodified command-line arguments.
    /// </summary>
    public string[] Args { get; }

    /// <summary>
    /// Creates an argument holder.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public BuildTasksCommandLineArgs(string[] args)
    {
        Args = args;
    }
}
