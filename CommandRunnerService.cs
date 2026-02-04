using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WindowsServiceUtils;

internal sealed class CommandRunnerService(
	ServiceConfig config,
	ILogger<CommandRunnerService> logger
) : BackgroundService {
	private Process? _process;

	public override async Task StopAsync(CancellationToken cancellationToken) {
		if (_process is { HasExited: false } proc) {
			logger.LogInformation($"Stopping process (PID {proc.Id})...");
			try {
				KillProcessTree(proc);
			}
			catch (InvalidOperationException) {
				// Process already exited
			}
			try {
				await WaitForProcessExitAsync(proc, cancellationToken);
			}
			catch (OperationCanceledException) {
				// Shutdown timeout exceeded
			}
			logger.LogInformation("Process stopped.");
		}
		await base.StopAsync(cancellationToken);
	}

	public override void Dispose() {
		_process?.Dispose();
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		(string fileName, string arguments) = ParseCommandLine(config.CommandLine);
		logger.LogInformation("Starting process: {FileName} {Arguments}", fileName, arguments);
		var startInfo = new ProcessStartInfo {
			FileName = fileName,
			Arguments = arguments,
			WorkingDirectory = config.WorkingDirectory ?? Path.GetDirectoryName(fileName)!,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = config.StdoutLogFile is not null,
			RedirectStandardError = config.StderrLogFile is not null
		};
		if (config.EnvironmentVariables is { Count: > 0 }) {
			foreach (var kvp in config.EnvironmentVariables)
				startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
		}
		_process = new Process { StartInfo = startInfo };

		StreamWriter? stdoutWriter = null;
		StreamWriter? stderrWriter = null;
		try {
			if (config.StdoutLogFile is { } stdoutPath) {
				string? dir = Path.GetDirectoryName(stdoutPath);
				if (dir is not null)
					Directory.CreateDirectory(dir);
				stdoutWriter = new StreamWriter(stdoutPath, true) { AutoFlush = true };
			}
			if (config.StderrLogFile is { } stderrPath) {
				string? dir = Path.GetDirectoryName(stderrPath);
				if (dir is not null)
					Directory.CreateDirectory(dir);
				stderrWriter = new StreamWriter(stderrPath, true) { AutoFlush = true };
			}
			if (!_process.Start()) {
				logger.LogError("Failed to start process: {FileName}", fileName);
				return;
			}
			logger.LogInformation("Process started with PID {Pid}", _process.Id);

			// Start async output readers
			var tasks = new List<Task>();
			if (stdoutWriter is not null)
				tasks.Add(PipeOutputAsync(_process.StandardOutput, stdoutWriter, stoppingToken));
			if (stderrWriter is not null)
				tasks.Add(PipeOutputAsync(_process.StandardError, stderrWriter, stoppingToken));
			tasks.Add(WaitForProcessExitAsync(_process, stoppingToken));

			try {
				await Task.WhenAll(tasks);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
				// Service is stopping — handled in StopAsync
			}

			if (_process.HasExited)
				logger.LogWarning("Process exited with code {ExitCode}", _process.ExitCode);
		}
		finally {
			stdoutWriter?.Dispose();
			stderrWriter?.Dispose();
		}
	}

	private static async Task PipeOutputAsync(StreamReader reader, StreamWriter writer, CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			string? line = await reader.ReadLineAsync();
			if (line is null)
				break;
			await writer.WriteLineAsync(line);
		}
	}

	private static Task WaitForProcessExitAsync(Process process, CancellationToken ct) {
		if (process.HasExited)
			return Task.CompletedTask;
		var tcs = new TaskCompletionSource<bool>();
		process.EnableRaisingEvents = true;
		process.Exited += (_, _) => tcs.TrySetResult(true);
		if (ct.CanBeCanceled)
			ct.Register(() => tcs.TrySetCanceled());
		// Handle race: process may have exited between check and event subscription
		if (process.HasExited)
			tcs.TrySetResult(true);
		return tcs.Task;
	}

	private static void KillProcessTree(Process process) {
		try {
			using var taskkill = Process.Start(
				new ProcessStartInfo {
					FileName = "taskkill",
					Arguments = $"/T /F /PID {process.Id}",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}
			);
			taskkill?.WaitForExit(5000);
		}
		catch {
			try {
				process.Kill();
			}
			catch { }
		}
	}

	private static (string FileName, string Arguments) ParseCommandLine(string commandLine) {
		var span = commandLine.AsSpan().Trim();
		if (span.Length == 0)
			throw new ArgumentException("Command line is empty.", nameof(commandLine));
		string fileName;
		string arguments;
		if (span[0] == '"') {
			int closeQuote = span[1..].IndexOf('"');
			if (closeQuote < 0) {
				fileName = span[1..].ToString();
				arguments = string.Empty;
			}
			else {
				fileName = span[1..(closeQuote + 1)].ToString();
				arguments = span[(closeQuote + 2)..].Trim().ToString();
			}
		}
		else {
			int idx = span.IndexOf(' ');
			if (idx < 0) {
				fileName = span.ToString();
				arguments = string.Empty;
			}
			else {
				fileName = span[..idx].ToString();
				arguments = span[(idx + 1)..].Trim().ToString();
			}
		}
		ResolveInterpreter(ref fileName, ref arguments);
		return (fileName, arguments); 
	}

	private static void ResolveInterpreter(ref string fileName, ref string arguments) {
		switch (Path.GetExtension(fileName).ToLower()) {
			case ".cmd" or ".bat": {
				string args = $"/c \"{fileName}\"";
				if (arguments.Length > 0)
					args += " " + arguments;
				fileName = "cmd.exe";
				arguments = args;
				break;
			}
			case ".ps1": {
				string args = $"-NoProfile -File \"{fileName}\"";
				if (arguments.Length > 0)
					args += " " + arguments;
				fileName = "powershell.exe";
				arguments = args;
				break;
			}
		}
	}
}