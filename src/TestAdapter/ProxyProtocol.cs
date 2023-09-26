namespace GodotSharp.TestAdapter;

/// <summary>
/// Messages passed between the test adapter host and test proxy server
/// </summary>
public partial class ProxyProtocol {
	/// <summary>
	/// Base message
	/// </summary>
	public abstract class Message { }

	/// <summary>
	/// Executes a test on the remote test proxy server
	/// </summary>
	public class RunTest : Message {
		/// <summary>
		/// The assembly path of the discovered test. This should be the full
		/// filesystem path of the discovered test assembly so the test proxy
		/// can load and locate the target class and method. This implies the 
		/// test proxy project has a reference to the target assembly.
		/// </summary>
		public string AssemblyPath { get; set; } = "";

		/// <summary>
		/// The fully qualified class/path name of the test
		/// </summary>
		public string FullyQualifiedName { get; set; } = "";
	}

	/// <summary>
	/// Result of a test execution from the remote test proxy server
	/// </summary>
	public class RunResult : Message {
		/// <summary>
		/// Indicates if this test passed or failed on the remote test proxy server.
		/// If an exception is thrown on the proxy, this will be false.
		/// </summary>
		public bool Passed { get; set; }

		/// <summary>
		/// Error message if the test failed, if any
		/// </summary>
		public string ErrorMessage { get; set; } = "";

		/// <summary>
		/// Stack trace if the test failed, if any
		/// </summary>
		public string ErrorStackTrace { get; set; } = "";
	}
}