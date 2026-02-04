using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsServiceUtils;

internal sealed class ServiceStartTypeConverter() : JsonStringEnumConverter<ServiceStartType>(Policy) {
	private static readonly JsonNamingPolicy Policy = JsonNamingPolicy.KebabCaseLower;

	public static ServiceStartType Parse(string value) {
		foreach (string name in Enum.GetNames(typeof(ServiceStartType))) {
			if (string.Equals(Policy.ConvertName(name), value, StringComparison.OrdinalIgnoreCase))
				return (ServiceStartType)Enum.Parse(typeof(ServiceStartType), name);
		}
		throw new ArgumentException($"Invalid start type: '{value}'.");
	}
}

[JsonConverter(typeof(ServiceStartTypeConverter))]
internal enum ServiceStartType : byte {
	Manual,
	Auto,
	Disabled,
	DelayedAuto
}

internal sealed class ServiceConfig {
	public required string Name { get; set; }

	public string? DisplayName { get; set; }

	public required string CommandLine { get; set; }

	public string? WorkingDirectory { get; set; }

	public string? Description { get; set; }

	public ServiceStartType StartType { get; set; } = ServiceStartType.Manual;

	public string[]? Dependencies { get; set; }

	public string? Username { get; set; }

	public string? Password { get; set; }

	public Dictionary<string, string>? EnvironmentVariables { get; set; }

	public string? StdoutLogFile { get; set; }

	public string? StderrLogFile { get; set; }

	public static ServiceConfig Load(string name) {
		string path = Paths.GetConfigPath(name);
		if (!File.Exists(path))
			throw new FileNotFoundException($"No configuration found for service '{name}'.", path);
		string json = File.ReadAllText(path);
		return JsonSerializer.Deserialize(json, ServiceConfigContext.Default.ServiceConfig)
			?? throw new InvalidOperationException($"Failed to deserialize configuration for service '{name}'.");
	}

	public static void Delete(string name) {
		string path = Paths.GetConfigPath(name);
		if (File.Exists(path))
			File.Delete(path);
	}

	public void Save() {
		Directory.CreateDirectory(Paths.ConfigsFolder);
		string json = JsonSerializer.Serialize(this, ServiceConfigContext.Default.ServiceConfig);
		File.WriteAllText(Paths.GetConfigPath(Name), json);
	}
}

[JsonSourceGenerationOptions(
	WriteIndented = true,
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ServiceConfig))]
internal sealed partial class ServiceConfigContext : JsonSerializerContext;