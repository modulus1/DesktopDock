namespace DesktopDock.Tests;

/// <summary>
/// A tiny test harness. The dock has no test-framework dependency so the whole
/// solution builds and its tests run with nothing but the .NET SDK.
/// </summary>
public static class Harness
{
    private static readonly List<string> Failures = new();
    private static int passed;

    public static void Test(string name, Action body)
    {
        try
        {
            body();
            passed++;
        }
        catch (Exception exception)
        {
            Failures.Add($"{name}: {exception.Message}");
        }
    }

    public static void AreEqual<T>(T expected, T actual, string because = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"expected <{Describe(expected)}> but got <{Describe(actual)}> {because}".TrimEnd());
        }
    }

    public static void IsTrue(bool condition, string because = "")
    {
        if (!condition)
        {
            throw new InvalidOperationException($"expected true {because}".TrimEnd());
        }
    }

    public static void IsFalse(bool condition, string because = "") => IsTrue(!condition, because);

    public static void IsNull(object? value, string because = "")
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"expected null but got <{Describe(value)}> {because}".TrimEnd());
        }
    }

    public static T NotNull<T>(T? value, string because = "")
        where T : class
        => value ?? throw new InvalidOperationException($"expected a value {because}".TrimEnd());

    public static int Report()
    {
        foreach (string failure in Failures)
        {
            Console.WriteLine($"FAIL  {failure}");
        }

        Console.WriteLine($"\n{passed} passed, {Failures.Count} failed");
        return Failures.Count == 0 ? 0 : 1;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => text,
        IEnumerable<string> items => string.Join(", ", items),
        _ => value.ToString() ?? "?",
    };
}
