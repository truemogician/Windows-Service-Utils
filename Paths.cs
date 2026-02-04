namespace WindowsServiceUtils;

internal static class Paths {
	private static readonly string DataFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
		nameof(WindowsServiceUtils)
	);

	public static string GetDataFolder() => DataFolder;

	public static string GetConfigsFolder() =>
		Path.Combine(DataFolder, "configs");

	public static string GetConfigPath(string name) =>
		Path.Combine(DataFolder, "configs", $"{name}.json");
}