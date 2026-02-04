using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsServiceUtils;

internal sealed record InterpreterEntry(string Exec, string Args = "\"{file}\"");

internal static class InterpreterConfig {
	private static readonly Dictionary<string, InterpreterEntry> Defaults = new(StringComparer.OrdinalIgnoreCase) {
		[".cmd"] = new("cmd.exe", "/c \"{file}\""),
		[".bat"] = new("cmd.exe", "/c \"{file}\""),
		[".ps1"] = new("powershell.exe", "-NoProfile -File \"{file}\""),
		[".py"] = new("python.exe"),
		[".js"] = new("node.exe"),
	};

	public static Dictionary<string, InterpreterEntry> Load() {
		var merged = new Dictionary<string, InterpreterEntry>(Defaults, StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in LoadCustom())
			merged[kvp.Key] = kvp.Value;
		return merged;
	}

	public static Dictionary<string, InterpreterEntry> LoadCustom() {
		string path = Paths.GetInterpreterConfigPath();
		if (!File.Exists(path))
			return new Dictionary<string, InterpreterEntry>(StringComparer.OrdinalIgnoreCase);
		string json = File.ReadAllText(path);
		return JsonSerializer.Deserialize(json, InterpreterConfigContext.Default.Interpreters)
			?? new Dictionary<string, InterpreterEntry>(StringComparer.OrdinalIgnoreCase);
	}

	public static void SaveCustom(Dictionary<string, InterpreterEntry> custom) {
		Directory.CreateDirectory(Paths.GetDataFolder());
		string json = JsonSerializer.Serialize(custom, InterpreterConfigContext.Default.Interpreters);
		File.WriteAllText(Paths.GetInterpreterConfigPath(), json);
	}

	public static InterpreterEntry? GetDefault(string extension) =>
		Defaults.TryGetValue(extension, out var entry) ? entry : null;
}

[JsonSourceGenerationOptions(
	WriteIndented = true,
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(Dictionary<string, InterpreterEntry>), TypeInfoPropertyName = "Interpreters")]
internal sealed partial class InterpreterConfigContext : JsonSerializerContext;
