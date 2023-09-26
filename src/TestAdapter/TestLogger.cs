namespace GodotSharp.TestAdapter;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

public class TestLogger {
	private IMessageLogger _logger;

	public TestLogger(IMessageLogger logger) {
		this._logger = logger;
	}

	public void Debug(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0
	) {
		this.Info(
			message: $"[{sourceFilePath}][{memberName}:{sourceLineNumber}]: {message}"
		);
	}

	public void Error(string message) {
		var stack = new StackTrace(true);
		this._logger.SendMessage(
			testMessageLevel: TestMessageLevel.Error,
			message: $"{message}\n{stack.GetFrame(1)}"
		);
	}

	public void Error(Exception e) {
		this._logger.SendMessage(
			testMessageLevel: TestMessageLevel.Error,
			message: $"{e}\n{e.StackTrace}"
		);
	}

	public void Info(string message) {
		this._logger.SendMessage(
			testMessageLevel: TestMessageLevel.Informational,
			message: message
		);
	}

	public void Warn(string message) {
		this._logger.SendMessage(
			testMessageLevel: TestMessageLevel.Warning,
			message: message
		);
	}
}