namespace Squirix.Analyzers.FrameworkCompiler;

/// <summary>
/// Breaks SQR0025 on purpose: the job that builds this project expects that diagnostic as proof that the analyzer ran.
/// </summary>
public static class Sample
{
    /// <summary>Returns a number although the name promises a Boolean result.</summary>
    public static int TryGet() => 0;
}
