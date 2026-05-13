namespace Cyberpilot.Pipeline;

internal sealed class PipelineConsoleWriter(TextWriter output)
{
    public void WriteHeader(string title)
    {
        output.WriteLine();
        output.WriteLine("============================================================");
        output.WriteLine(title);
        output.WriteLine("============================================================");
    }

    public void WriteStep(string message)
    {
        output.WriteLine($"[step] {message}");
    }

    public void WriteSuccess(string message)
    {
        output.WriteLine($"[ ok ] {message}");
    }

    public void WriteWarning(string message)
    {
        output.WriteLine($"[warn] {message}");
    }

    public void WriteFailure(string message)
    {
        output.WriteLine($"[fail] {message}");
    }

    public void WriteDetail(string name, string value)
    {
        output.WriteLine($"  {name,-14}: {value}");
    }

    public static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:0.##} min"
            : $"{duration.TotalSeconds:0.##} sec";
    }
}