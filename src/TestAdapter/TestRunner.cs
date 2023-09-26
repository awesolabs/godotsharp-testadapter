namespace GodotSharp.TestAdapter;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

/// <summary>
/// A poor attempt at a Godot Test Adapter to run tests under standard C# IDE(s)
/// RELATED: https://github.com/microsoft/vstest/
/// RELATED: https://github.com/microsoft/vstest/blob/main/docs/RFCs/0004-Adapter-Extensibility.md#writing-an-adapter
/// </summary>
[DefaultExecutorUri(ExecutorUri)]
[ExtensionUri(ExecutorUri)]
[FileExtension(".dll")]
[FileExtension(".exe")]
[Category("managed")]
public class TestRunner : ITestDiscoverer, ITestExecutor2 {
	/// <summary>
	/// Identifies this test adapter
	/// </summary>
	public const string ExecutorUri = "executor://godotsharp.testadapter/v1";

	/// <summary>
	/// The internal HTTP Client we use to contact the godot-side test proxy service
	/// TODO: should probably move this into an initalization method called by testfx
	/// </summary>
	private readonly HttpClient HttpClient = new() {
		Timeout = TimeSpan.FromHours(1),
	};

	/// <summary>
	/// Test Adapter Logger
	/// </summary>
	private TestLogger? Logger;

	/// <summary>
	/// Called when test executions are canceled by the user or testfx
	/// TODO: implement cancelation
	/// </summary>
	public void Cancel() { }

	/// <summary>
	/// NOTE: this seems to be called when invoking tests via vscode RunTest/DebugTest
	/// NOTE: vscode has some logs indicating that testhost.dll / testhost.exe invokes this #research
	/// NOTE: sources is a list of DLLs or perhaps exes to search
	/// </summary>
	/// <param name="sources"></param>
	/// <param name="discoveryContext"></param>
	/// <param name="logger"></param>
	/// <param name="discoverySink"></param>
	public void DiscoverTests(
		IEnumerable<string> sources,
		IDiscoveryContext discoveryContext,
		IMessageLogger logger,
		ITestCaseDiscoverySink discoverySink
	) {
		this.Logger = new TestLogger(logger: logger);
		this.Logger.Info(message: "Starting Test Discovery");
		var testcases = this.SearchForTestCases(
			sources: sources
		);
		testcases.ToList().ForEach(
			action: discoverySink.SendTestCase
		);
	}

	/// <summary>
	/// Not sure on the conditions of execution here
	/// </summary>
	/// <param name="tests"></param>
	/// <param name="runContext"></param>
	/// <param name="frameworkHandle"></param>
	public void RunTests(
		IEnumerable<TestCase>? tests,
		IRunContext? runContext,
		IFrameworkHandle? frameworkHandle
	) {
		tests = tests ?? throw new ArgumentNullException(
			paramName: nameof(tests)
		);
		frameworkHandle = frameworkHandle ?? throw new ArgumentNullException(
			paramName: nameof(frameworkHandle)
		);
		runContext = runContext ?? throw new ArgumentNullException(
			paramName: nameof(runContext)
		);
		this.Logger = new TestLogger(
			logger: frameworkHandle
		);
		var executions = new List<Task>();		
		foreach (var test in tests) {			
			try {				
				frameworkHandle.RecordStart(
					testCase: test
				);
				var testConfig = this.LoadTestConfig();
				var startInfo = new ProcessStartInfo {
					FileName = testConfig.GodotExecutablePath,
					WorkingDirectory = testConfig.GodotProjectDirectory,
					Arguments = @"--headless addons/GodotSharp.TestAdapter/TestProxy.tscn",
					UseShellExecute = true,
					CreateNoWindow = true
				};
				var process = new Process {
					StartInfo = startInfo
				};
				process.Start();
				if (runContext.IsBeingDebugged && frameworkHandle is IFrameworkHandle2 fh2) {
					fh2.AttachDebuggerToProcess(
						pid: process.Id
					);
				}
				var execution = Task.Run(
					function: () => this.ExecuteTest(
						testCase: test,
						handle: frameworkHandle,
						proxyProcessId: process.Id
					)
				).ContinueWith(
					continuationAction: (Task<TestResult> result) => {
						process.Kill();
						frameworkHandle.RecordResult(
							testResult: result.Result
						);
						frameworkHandle.RecordEnd(
							testCase: test,
							outcome: result.Result.Outcome
						);
					}
				);
				executions.Add(
					item: execution
				);
			}
			catch (Exception e) {
				var result = new TestResult(testCase: test) {
					DisplayName = test.DisplayName,
					EndTime = DateTime.UtcNow,
					ErrorMessage = e.Message,
					ErrorStackTrace = e.StackTrace,
					Outcome = TestOutcome.Failed,
				};
				frameworkHandle.RecordResult(
					testResult: result
				);
				frameworkHandle.RecordEnd(
					testCase: test,
					outcome: TestOutcome.Failed
				);
			}
		}
		Task.WhenAll(tasks: executions).GetAwaiter().GetResult();
	}

	/// <summary>
	/// Not sure on the conditions of execution here
	/// </summary>
	/// <param name="sources"></param>
	/// <param name="runContext"></param>
	/// <param name="frameworkHandle"></param>
	public void RunTests(
		IEnumerable<string>? sources,
		IRunContext? runContext,
		IFrameworkHandle? frameworkHandle
	) {
		sources = sources ?? throw new ArgumentNullException(
			paramName: nameof(sources)
		);
		frameworkHandle = frameworkHandle ?? throw new ArgumentNullException(
			paramName: nameof(frameworkHandle)
		);
		this.Logger = new TestLogger(logger: frameworkHandle);
		var testCases = this.SearchForTestCases(
			sources: sources
		);
		this.RunTests(
			tests: testCases,
			runContext: runContext,
			frameworkHandle: frameworkHandle
		);
	}

	/// <summary>
	/// Indicates if the debugger should attach to the test host
	/// </summary>
	/// <param name="sources"></param>
	/// <param name="runContext"></param>
	/// <returns></returns>
	public bool ShouldAttachToTestHost(
		IEnumerable<string>? sources,
		IRunContext runContext
	) {
		return true;
	}

	/// <summary>
	/// Indicates if the debugger should attach to the test host
	/// </summary>
	/// <param name="tests"></param>
	/// <param name="runContext"></param>
	/// <returns></returns>
	public bool ShouldAttachToTestHost(
		IEnumerable<TestCase>? tests,
		IRunContext runContext
	) {
		return true;
	}

	/// <summary>
	/// Executes the test case by calling the test proxy server
	/// </summary>
	/// <param name="testCase"></param>
	/// <param name="proxyProcessId"></param>
	/// <returns></returns>
	private async Task<TestResult> ExecuteTest(
		TestCase testCase,
		IFrameworkHandle handle,
		int proxyProcessId
	) {
		DateTime end;
		var start = DateTime.UtcNow;
		var alive = false;
		var proxyuri = new Uri(
			uriString: $"http://127.0.0.1:{proxyProcessId}"
		);
		var result = new TestResult(testCase: testCase) {
			DisplayName = testCase.DisplayName,			
			StartTime = start
		};
		try {
			for (var a = 0 ; a < 10 ; a++) {
				var resp = await this.HttpClient.GetAsync(
					requestUri: $"{proxyuri}alive"
				);
				var msg = await resp.Content.ReadAsStringAsync();
				if (msg != "1") {
					await Task.Delay(millisecondsDelay: 1000);
					continue;
				}
				alive = true;
				break;
			}
			if (!alive) {
				throw new Exception(
					message: $"failed to connect to text proxy server '{proxyuri}'"
				);
			}
			var response = await this.HttpClient.PostAsJsonAsync(
				requestUri: $"{proxyuri}run-test",
				value: new ProxyProtocol.RunTest {
					AssemblyPath = testCase.Source,
					FullyQualifiedName = testCase.FullyQualifiedName
				}
			);
			var runResult = await response.Content
				.ReadFromJsonAsync<ProxyProtocol.RunResult>();
			result.Outcome = runResult!.Passed
				? TestOutcome.Passed
				: TestOutcome.Failed;
			result.ErrorMessage = runResult.ErrorMessage;
			result.ErrorStackTrace = runResult.ErrorStackTrace;
		}
		catch (Exception e) {			
			result.Outcome = TestOutcome.Failed;
			result.ErrorMessage = e.ToString();
			result.ErrorStackTrace = e.StackTrace;
		}
		finally {
			end = DateTime.UtcNow;
			result.EndTime = end;
			result.Duration = end - start;
		}
		return result;
	}

	/// <summary>
	/// Searches for test cases by supplied source dll file paths
	/// </summary>
	/// <param name="sources"></param>
	/// <returns></returns>
	private IEnumerable<TestCase> SearchForTestCases(IEnumerable<string> sources) {
		var assemblies = sources.Select(
			selector: source => (
				Assembly: Assembly.LoadFile(path: source),
				Source: source
			)
		);
		return assemblies.SelectMany(
			selector: asource => this.SearchForTestClasses(
				assembly: asource.Assembly,
				source: asource.Source
			)
		);
	}

	/// <summary>
	/// Attempts to search for valid test classes in the provided assembly, returning
	/// mapped TestCases
	/// </summary>
	/// <param name="assembly"></param>
	/// <returns></returns>
	private IEnumerable<TestCase> SearchForTestClasses(Assembly assembly, string source) {
		var testClasses = assembly.GetTypes().Where(
			predicate: rtype => rtype.GetCustomAttributes<TestClassAttribute>().Any()
		);
		return testClasses.SelectMany(
			selector: testClass => this.SearchForTestMethods(
				testClass: testClass,
				source: source
			)
		);
	}

	/// <summary>
	/// Search for all test methods of a test class
	/// </summary>
	/// <param name="assembly"></param>
	/// <param name="testClass"></param>
	/// <returns></returns>
	private IEnumerable<TestCase> SearchForTestMethods(
		Type testClass,
		string source
	) {
		var testMethods = testClass.GetMethods().Where(
			predicate: methodInfo => methodInfo.GetCustomAttributes<TestMethodAttribute>().Any()
		);
		var testCases = testMethods.Select(
			selector: testMethod => this.MapTestCaseFromMethod(
				testClass: testClass,
				testMethod: testMethod,
				source: source
			)
		);
		return testCases.Select(selector: a => a);
	}

	/// <summary>
	/// Mapping of test class method to test case
	/// </summary>
	/// <param name="assembly"></param>
	/// <param name="testClass"></param>
	/// <param name="testMethod"></param>
	/// <param name="testCase"></param>
	private TestCase MapTestCaseFromMethod(
		Type testClass,
		MethodInfo testMethod,
		string source
	) {
		var result = new TestCase {
			DisplayName = $"{testClass.FullName}.{testMethod.Name}",
			ExecutorUri = new Uri(uriString: ExecutorUri),
			FullyQualifiedName = $"{testClass.FullName}.{testMethod.Name}",
			Source = source,
			CodeFilePath = Assembly.GetAssembly(type: testClass)?.Location ?? "unable to locate file",
		};
		return result;
	}

	/// <summary>
	/// Loads the Test Adapter configuration from the godot project file
	/// </summary>
	/// <returns></returns>
	private TestConfig LoadTestConfig() {
		var godotProjectFile = this.LocateGodotProjectFile(
			startingDirectory: Directory.GetCurrentDirectory()
		);
		var config = new TestConfig {
			GodotExecutablePath = this.LocateGodotExecutable(
				godotProjectFile: godotProjectFile
			),
			GodotProjectDirectory = this.LocateGodotProjectDirectory(
				godotProjectFile: godotProjectFile
			)
		};
		return config;
	}

	/// <summary>
	/// Locates the godot project directory
	/// </summary>
	/// <param name="godotProjectFile"></param>
	/// <returns></returns>
	private string LocateGodotProjectDirectory(string godotProjectFile) {
		if (!File.Exists(path: godotProjectFile)) {
			throw new FileNotFoundException(
				message: $"Godot project file '{godotProjectFile}' does not exist"
			);
		}
		return Directory.GetParent(path: godotProjectFile)!.FullName;
	}

	/// <summary>
	/// Locates the appropriate godot executable path from the godot project settings
	/// </summary>
	/// <param name="godotProjectFile"></param>
	/// <returns></returns>
	/// <exception cref="FileNotFoundException"></exception>
	/// <exception cref="NotSupportedException"></exception>
	/// <exception cref="InvalidDataException"></exception>
	private string LocateGodotExecutable(string godotProjectFile) {
		if (!File.Exists(path: godotProjectFile)) {
			throw new FileNotFoundException(
				message: $"Godot project file '{godotProjectFile}' does not exist"
			);
		}
		var lines = File.ReadAllLines(
			path: godotProjectFile
		);
		var exePath = string.Empty;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			exePath = lines.FirstOrDefault(
				predicate: a => a.StartsWith(
					value: "testadapter/exe_path_windows"
				)
			) ?? string.Empty;
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			exePath = lines.FirstOrDefault(
				predicate: a => a.StartsWith(
					value: "testadapter/exe_path_linux"
				)
			) ?? string.Empty;
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			exePath = lines.FirstOrDefault(
				predicate: a => a.StartsWith(
					value: "testadapter/exe_path_macosx"
				)
			) ?? string.Empty;
		}
		else {
			throw new NotSupportedException(
				message: $"OS not supported"
			);
		}
		exePath = exePath.Split(separator: "=")
			.LastOrDefault(defaultValue: "")
			.TrimStart(trimChar: '"')
			.TrimEnd(trimChar: '"');
		if (string.IsNullOrWhiteSpace(value: exePath)) {
			throw new InvalidDataException(
				message: "Godot Executable path null or empty. Ensure godot executable set in project settings"
			);
		}
		exePath = Environment.ExpandEnvironmentVariables(name: exePath);
		if (!File.Exists(path: exePath)) {
			throw new InvalidDataException(
				message: $"Godot Executable path '{exePath}' does not exist"
			);
		}
		return exePath;
	}

	/// <summary>
	/// Locates the godot project file by searching the local filesystem
	/// </summary>
	/// <param name="startingDirectory"></param>
	/// <returns></returns>
	/// <exception cref="InvalidDataException"></exception>
	/// <exception cref="FileNotFoundException"></exception>
	private string LocateGodotProjectFile(string startingDirectory) {
		var searchedDirectories = new List<string>();
		var searchStack = new Stack<string>();
		searchStack.Push(item: startingDirectory);
		while (searchStack.Count > 0) {
			var currentDirectory = searchStack.Pop();
			var searched = searchedDirectories.Any(
				predicate: a => a == currentDirectory
			);
			if (searched) {
				continue;
			}
			searchedDirectories.Add(
				item: currentDirectory
			);
			var cwdFiles = Directory.GetFiles(
				path: currentDirectory
			);
			var hasGodotFile = cwdFiles.Any(
				predicate: a => a.EndsWith(value: "project.godot")
			);
			if (hasGodotFile) {
				return Path.Combine(
					path1: currentDirectory,
					path2: "project.godot"
				);
			}
			var hasSlnFile = cwdFiles.Any(
				predicate: a => a.EndsWith(value: ".sln")
			);
			if (hasSlnFile) {
				Directory.GetDirectories(path: currentDirectory).ToList().ForEach(
					action: searchStack.Push
				);
				continue;
			}
			var parentDirectory = Directory.GetParent(
				path: currentDirectory
			);
			parentDirectory = parentDirectory ?? throw new InvalidDataException(
				message: $"parent search directory '{parentDirectory}' is null"
			);
			searchStack.Push(
				item: parentDirectory.FullName
			);
		}
		var pathsSearched = string.Join(
			separator: '\n',
			values: searchedDirectories
		);
		throw new FileNotFoundException(
			message: $"Unable to locate godot project file. Paths searched:\n{pathsSearched}"
		);
	}
}