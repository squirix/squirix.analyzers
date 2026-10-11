namespace Squirix.Analyzers.FrameworkCompiler;

/// <summary>
/// Breaks SQR0025 on purpose. The "Framework compiler" CI job compiles this file with the compiler that runs on .NET Framework and
/// expects that diagnostic as proof that the analyzer was loaded. The file belongs to no project.
/// </summary>
public static class Sample
{
    /// <summary>Returns a number although the name promises a Boolean result.</summary>
    public static int TryGet() => 0;
}
