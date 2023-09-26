namespace GodotSharp.TestAdapter;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Application side test proxy used to recieve test run requests and return test
/// results back to MSTest.Framework test host adapter
/// </summary>
public partial class TestProxy : Node {
	/// <summary>
	/// The process id of this test proxy
	/// </summary>
	private int processId;		

	/// <summary>
	/// Proxy http server instance
	/// </summary>
	private WebApplication? testServer;

	/// <summary>
	/// Called upon godot startup to initialize the test proxy
	/// </summary>
	public override void _Ready() {
		this.processId = Process.GetCurrentProcess().Id;
		this.userTempPath = Path.GetTempPath();		
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.ConfigureKestrel(
			options: serveropts => {
				serveropts.Limits.KeepAliveTimeout = TimeSpan.FromHours(value: 1);
				// TODO: change to named pipes when .NET 8 lands otherwise
				// we are liable to run into pid <-> port mapping issues					
				// serveropts.ListenNamedPipe(this._pipeName);
				// TODO: move to locahost bind
				serveropts.ListenAnyIP(
					port: this.processId
				);
			}
		);
		this.testServer = builder.Build();
		this.testServer.MapGet(
			pattern: "/alive",
			handler: () => {
				return "1";
			}
		);
		this.testServer.MapPost(
			pattern: "/run-test",
			handler: ([FromBody] ProxyProtocol.RunTest test) => {
				var result = this.ExecuteTest(test: test);
				var hresult = Results.Json(data: result);
				return hresult;
			}
		);
		GD.Print(what: $"Starting Test Proxy Server");
		this.testServer.StartAsync();
	}

	/// <summary>
	/// Executes an incoming test run request
	/// </summary>
	/// <param name="test"></param>
	/// <returns></returns>
	public ProxyProtocol.RunResult ExecuteTest(ProxyProtocol.RunTest test) {
		var result = new ProxyProtocol.RunResult();
		try {
			var testAssembly = Assembly.LoadFrom(
				assemblyFile: test.AssemblyPath
			);
			var searchAssemblies = new List<Assembly>() {
				Assembly.GetExecutingAssembly(),
				testAssembly,
			};
			var lastDotIndex = test.FullyQualifiedName.LastIndexOf(
				value: '.'
			);
			var className = test.FullyQualifiedName.Substring(
				startIndex: 0,
				length: lastDotIndex
			);
			var methodName = test.FullyQualifiedName.Substring(
				startIndex: lastDotIndex + 1
			);
			var classType = searchAssemblies
				.Select(selector: a => a.GetType(name: className))
				.Where(predicate: a => a != null)
				.FirstOrDefault();
			if (classType == null) {
				result.ErrorMessage = $"No such class {className}";
				return result;
			}
			var classInstance = Activator.CreateInstance(
				type: classType
			);
			var methodInfo = classType.GetMethod(
				name: methodName
			);
			if (methodInfo == null) {
				result.ErrorMessage = $"No such class method {test.FullyQualifiedName}";
				return result;
			}
			methodInfo.Invoke(
				obj: classInstance,
				parameters: null
			);
			result.Passed = true;
		}
		catch (Exception e) {
			result.ErrorMessage = e.ToString();
			result.ErrorStackTrace = e.StackTrace ?? "";
			result.Passed = false;
		}
		return result;
	}
}