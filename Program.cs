using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WindowsServiceUtils;

static Argument<string> CreateNameArg() => new("name") { Description = "Service name" };

var root = new RootCommand("Windows Service Utils — register any command line as a Windows Service");

#region Run
var runCmd = new Command("run", "Execute a registered service (used by SCM)");
var runNameArg = CreateNameArg();
runCmd.Arguments.Add(runNameArg);
runCmd.SetAction(async (result, ct) => {
	string name = result.GetValue(runNameArg)!;
	var config = ServiceConfig.Load(name);
	var host = Host.CreateDefaultBuilder()
		.UseWindowsService()
		.ConfigureServices(services => {
			services.AddSingleton(config);
			services.AddHostedService<CommandRunnerService>();
		})
		.Build();
	await host.RunAsync(ct);
	return 0;
});
root.Subcommands.Add(runCmd);
#endregion

#region Add service
var addCmd = new Command("add", "Register a new Windows Service");
var addNameArg = CreateNameArg();
var commandOpt = new Option<string>("--command", "-c") { Description = "Command line to execute", Required = true };
var displayNameOpt = new Option<string?>("--display-name", "-d") { Description = "Display name shown in services.msc" };
var workingDirOpt = new Option<string?>("--working-directory", "-w") { Description = "Working directory for the child process" };
var descriptionOpt = new Option<string?>("--description") { Description = "Service description" };
var startTypeOpt = new Option<string>("--start-type") { Description = "Start type: auto, manual, disabled, delayed-auto", DefaultValueFactory = _ => "manual" };
var dependOpt = new Option<string?>("--depend") { Description = "Comma-separated service dependencies" };
var usernameOpt = new Option<string?>("--username") { Description = "Service account username (default: LocalSystem)" };
var passwordOpt = new Option<string?>("--password") { Description = "Service account password" };
var visibleOpt = new Option<bool>("--visible") { Description = "Show the child process window (default: hidden)" };
var envOpt = new Option<string[]?>("--env") { Description = "Environment variable in KEY=VALUE format (repeatable)" };
var stdoutLogOpt = new Option<string?>("--stdout-log") { Description = "File path for stdout logging" };
var stderrLogOpt = new Option<string?>("--stderr-log") { Description = "File path for stderr logging" };

addCmd.Arguments.Add(addNameArg);
addCmd.Options.Add(commandOpt);
addCmd.Options.Add(displayNameOpt);
addCmd.Options.Add(workingDirOpt);
addCmd.Options.Add(descriptionOpt);
addCmd.Options.Add(startTypeOpt);
addCmd.Options.Add(dependOpt);
addCmd.Options.Add(usernameOpt);
addCmd.Options.Add(passwordOpt);
addCmd.Options.Add(visibleOpt);
addCmd.Options.Add(envOpt);
addCmd.Options.Add(stdoutLogOpt);
addCmd.Options.Add(stderrLogOpt);

addCmd.SetAction(result => {
	var name = result.GetValue(addNameArg)!;
	var command = result.GetValue(commandOpt)!;
	var displayName = result.GetValue(displayNameOpt);
	var workingDir = result.GetValue(workingDirOpt);
	var description = result.GetValue(descriptionOpt);
	var startType = result.GetValue(startTypeOpt)!;
	var depend = result.GetValue(dependOpt);
	var username = result.GetValue(usernameOpt);
	var password = result.GetValue(passwordOpt);
	var envVars = result.GetValue(envOpt);
	var stdoutLog = result.GetValue(stdoutLogOpt);
	var stderrLog = result.GetValue(stderrLogOpt);
	var startTypeEnum = ServiceStartTypeConverter.Parse(startType);

	Dictionary<string, string>? env = null;
	if (envVars is { Length: > 0 }) {
		env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in envVars) {
			int eq = entry.IndexOf('=');
			if (eq < 0)
				throw new ArgumentException($"Invalid environment variable format: '{entry}'. Expected KEY=VALUE.");
			env[entry[..eq]] = entry[(eq + 1)..];
		}
	}

	// Save config
	var config = new ServiceConfig {
		Name = name,
		DisplayName = displayName,
		CommandLine = command,
		WorkingDirectory = workingDir,
		Description = description,
		StartType = startTypeEnum,
		Dependencies = depend?.Split([','], StringSplitOptions.RemoveEmptyEntries)
			.Select(s => s.Trim())
			.ToArray(),
		Username = username,
		Password = password,
		EnvironmentVariables = env,
		StdoutLogFile = stdoutLog,
		StderrLogFile = stderrLog
	};
	config.Save();

	// Register with SCM
	string exePath = Process.GetCurrentProcess().MainModule?.FileName
		?? throw new InvalidOperationException("Cannot determine executable path.");
	SC.Create(
		name,
		$"\"{exePath}\" run {name}",
		startTypeEnum,
		displayName,
		config.Dependencies,
		username,
		password
	);
	if (description is not null)
		SC.SetDescription(name, description);
	Console.WriteLine($"Service '{name}' registered successfully.");
	return 0;
});
root.Subcommands.Add(addCmd);
#endregion

#region Remove service
var removeCmd = new Command("remove", "Uninstall a service");
var removeNameArg = CreateNameArg();
removeCmd.Arguments.Add(removeNameArg);
removeCmd.SetAction(result => {
	string name = result.GetValue(removeNameArg)!;
	try {
		using var sc = new ServiceController(name);
		if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) {
			Console.WriteLine($"Stopping service '{name}'...");
			sc.Stop();
			sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
		}
	}
	catch (InvalidOperationException) {
		// Service doesn't exist in SCM — continue with cleanup
	}

	try {
		SC.Delete(name);
	}
	catch (Win32Exception ex) when (ex.NativeErrorCode == 1060) {
		Console.WriteLine($"Warning: service '{name}' not found in SCM, cleaning up config only.");
	}
	ServiceConfig.Delete(name);
	Console.WriteLine($"Service '{name}' removed.");
	return 0;
});
root.Subcommands.Add(removeCmd);
#endregion

#region Manage service
var startCmd = new Command("start", "Start a service");
var startNameArg = CreateNameArg();
startCmd.Arguments.Add(startNameArg);
startCmd.SetAction(result => {
	string name = result.GetValue(startNameArg)!;
	using var sc = new ServiceController(name);
	if (sc.Status == ServiceControllerStatus.Running) {
		Console.WriteLine($"Service '{name}' is already running.");
		return 0;
	}
	Console.WriteLine($"Starting service '{name}'...");
	sc.Start();
	sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
	Console.WriteLine($"Service '{name}' started.");
	return 0;
});
root.Subcommands.Add(startCmd);

var stopCmd = new Command("stop", "Stop a service");
var stopNameArg = CreateNameArg();
stopCmd.Arguments.Add(stopNameArg);
stopCmd.SetAction(result => {
	string name = result.GetValue(stopNameArg)!;
	using var sc = new ServiceController(name);
	if (sc.Status == ServiceControllerStatus.Stopped) {
		Console.WriteLine($"Service '{name}' is already stopped.");
		return 0;
	}
	Console.WriteLine($"Stopping service '{name}'...");
	sc.Stop();
	sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
	Console.WriteLine($"Service '{name}' stopped.");
	return 0;
});
root.Subcommands.Add(stopCmd);

var restartCmd = new Command("restart", "Restart a service");
var restartNameArg = CreateNameArg();
restartCmd.Arguments.Add(restartNameArg);
restartCmd.SetAction(result => {
	string name = result.GetValue(restartNameArg)!;
	using var sc = new ServiceController(name);
	if (sc.Status == ServiceControllerStatus.Running) {
		Console.WriteLine($"Stopping service '{name}'...");
		sc.Stop();
		sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
	}
	Console.WriteLine($"Starting service '{name}'...");
	sc.Start();
	sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
	Console.WriteLine($"Service '{name}' restarted.");
	return 0;
});
root.Subcommands.Add(restartCmd);
#endregion

#region Interpreter config
var interpreterCmd = new Command("interpreter", "Manage script interpreter mappings");

var intListCmd = new Command("list", "List all interpreter mappings");
intListCmd.SetAction(_ => {
	foreach (var kvp in InterpreterConfig.EnumerateInterpreters().OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
		Console.WriteLine($"  {kvp.Key,-8} {kvp.Value.Exec} {kvp.Value.Args}");
	return 0;
});
interpreterCmd.Subcommands.Add(intListCmd);

var intSetCmd = new Command("set", "Set an interpreter mapping for a file extension");
var intExtArg = new Argument<string>("extension") { Description = "File extension (e.g. .py)" };
var intExecOpt = new Option<string>("--exec", "-e") { Description = "Interpreter executable", Required = true };
var intArgsOpt = new Option<string?>("--args", "-a") { Description = "Arguments template. Use {file} as placeholder for the script path. Default: \"{file}\"" };
intSetCmd.Arguments.Add(intExtArg);
intSetCmd.Options.Add(intExecOpt);
intSetCmd.Options.Add(intArgsOpt);
intSetCmd.SetAction(result => {
	var ext = result.GetValue(intExtArg)!;
	if (!ext.StartsWith("."))
		ext = "." + ext;
	var exec = result.GetValue(intExecOpt)!;
	var intArgs = result.GetValue(intArgsOpt) ?? "\"{file}\"";
	InterpreterConfig.SetInterpreter(ext, new(exec, intArgs));
	InterpreterConfig.Save();
	Console.WriteLine($"Interpreter for '{ext}' set to: {exec} {intArgs}");
	return 0;
});
interpreterCmd.Subcommands.Add(intSetCmd);

var intRemoveCmd = new Command("remove", "Remove a custom interpreter mapping");
var intRemoveExtArg = new Argument<string>("extension") { Description = "File extension to remove" };
intRemoveCmd.Arguments.Add(intRemoveExtArg);
intRemoveCmd.SetAction(result => {
	var ext = result.GetValue(intRemoveExtArg)!;
	if (!ext.StartsWith("."))
		ext = "." + ext;
	var entry = InterpreterConfig.Remove(ext);
	if (entry is null) {
		Console.WriteLine($"No custom interpreter mapping found for '{ext}'.");
		return 1;
	}
	InterpreterConfig.Save();
	Console.WriteLine($"Custom mapping for '{ext}' removed.");
	return 0;
});
interpreterCmd.Subcommands.Add(intRemoveCmd);

root.Subcommands.Add(interpreterCmd);
#endregion

return await root.Parse(args).InvokeAsync();