namespace WindowsServiceUtils;

internal static class Paths {
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        nameof(WindowsServiceUtils)
    );

	public static string InterpreterConfigPath => Path.Combine(DataFolder, "interpreter.json");

	public static string ConfigsFolder => Path.Combine(DataFolder, "configs");

    public static string GetConfigPath(string name) =>
		Path.Combine(DataFolder, "configs", $"{name}.json");
}