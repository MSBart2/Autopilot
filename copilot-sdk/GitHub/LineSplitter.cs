namespace Cyberpilot.GitHub;

internal static class LineSplitter
{
    public static string[] Split(string value)
    {
        return value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
