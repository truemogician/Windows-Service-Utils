using System.Diagnostics.CodeAnalysis;
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

	private static Dictionary<string, InterpreterEntry> Custom {
		get {
			if (field is not null)
				return field;
			string path = Paths.InterpreterConfigPath;
			if (!File.Exists(path))
				return field = new Dictionary<string, InterpreterEntry>(StringComparer.OrdinalIgnoreCase);
			string json = File.ReadAllText(path);
			return field = JsonSerializer.Deserialize(json, InterpreterConfigContext.Default.Interpreters)
				?? new Dictionary<string, InterpreterEntry>(StringComparer.OrdinalIgnoreCase);
        }
	}

	internal static string[] Extensions
		=> Defaults.Keys.Concat(Custom.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase).ToArray();

	internal static bool TryGetInterpreter(string extension, [MaybeNullWhen(false)] out InterpreterEntry entry)
		=> Custom.TryGetValue(extension, out entry) || Defaults.TryGetValue(extension, out entry);

	internal static InterpreterEntry? GetInterpreter(string extension)
		=> TryGetInterpreter(extension, out var entry) ? entry : null;

    internal static void SetInterpreter(string extension, InterpreterEntry entry)
		=> Custom[extension] = entry;

	internal static InterpreterEntry? Remove(string extension) {
		if (!Custom.TryGetValue(extension, out var entry))
			return null;
		Custom.Remove(extension);
		return entry;
	}

	internal static IEnumerable<KeyValuePair<string, InterpreterEntry>> EnumerateInterpreters() {
		foreach (var kvp in Custom)
			yield return kvp;
		foreach (var kvp in Defaults) {
			if (!Custom.ContainsKey(kvp.Key))
				yield return kvp; 
		}
	}

    internal static void Save() {
		Directory.CreateDirectory(Paths.DataFolder);
		string json = JsonSerializer.Serialize(Custom, InterpreterConfigContext.Default.Interpreters);
		File.WriteAllText(Paths.InterpreterConfigPath, json);
	}
}

[JsonSourceGenerationOptions(
	WriteIndented = true,
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(Dictionary<string, InterpreterEntry>), TypeInfoPropertyName = "Interpreters")]
internal sealed partial class InterpreterConfigContext : JsonSerializerContext;
