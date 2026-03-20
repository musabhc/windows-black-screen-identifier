using System.Diagnostics;

namespace BlackScreenIdentifier.Actions.Infrastructure;

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool Succeeded => ExitCode == 0;
}

public sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await outputTask.ConfigureAwait(false),
            StandardError = await errorTask.ConfigureAwait(false)
        };
    }
}
