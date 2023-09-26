namespace GodotSharp.TestAdapter;

/// <summary>
/// Test configuration for the project
/// </summary>
public class TestConfig {
	/// <summary>
	/// Path to the godot executable to use when executing tests
	/// </summary>
	public string GodotExecutablePath { get; set; } = string.Empty;

	/// <summary>
	/// Path to the godot project directory where the project.godot file exists
	/// </summary>
	public string GodotProjectDirectory { get; set; } = string.Empty;
}