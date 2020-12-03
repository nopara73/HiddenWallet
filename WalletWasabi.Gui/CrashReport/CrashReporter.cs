using System;
using System.Diagnostics;
using WalletWasabi.Logging;
using WalletWasabi.Microservices;
using WalletWasabi.Models;

namespace WalletWasabi.Gui.CrashReport
{
	public class CrashReporter
	{
		private const int MaxRecursiveCalls = 5;
		public int Attempts { get; private set; }
		public string Base64ExceptionString { get; private set; } = null;
		public bool IsReport { get; private set; }
		public bool HadException { get; private set; }
		public bool IsInvokeRequired => !IsReport && HadException;
		public SerializableException SerializedException { get; private set; }

		public void TryInvokeCrashReport()
		{
			try
			{
				if (Attempts >= MaxRecursiveCalls)
				{
					throw new InvalidOperationException($"The crash report has been called {MaxRecursiveCalls} times. Will not continue to avoid recursion errors.");
				}

				if (string.IsNullOrEmpty(Base64ExceptionString))
				{
					throw new InvalidOperationException($"The crash report exception message is empty.");
				}

				var mainExecutable = Process.GetCurrentProcess().MainModule?.FileName;
				var args = $"crashreport -attempt=\"{Attempts + 1}\" -exception=\"{Base64ExceptionString}\"";

				var startInfo = ProcessStartInfoFactory.Make(mainExecutable, args, true);
				using var p = Process.Start(startInfo);
			}
			catch (Exception ex)
			{
				Logger.LogWarning($"There was a problem while invoking crash report:{ex.ToUserFriendlyString()}.");
			}
		}

		public void SetShowCrashReport(string base64ExceptionString, int attempts)
		{
			Attempts = attempts;
			Base64ExceptionString = base64ExceptionString;
			SerializedException = SerializableException.FromBase64String(Base64ExceptionString);
			IsReport = true;
		}

		/// <summary>
		/// Sets the exception when it occurs the first time and should be reported to the user.
		/// </summary>
		public void SetException(Exception ex)
		{
			SerializedException = ex.ToSerializableException();
			Base64ExceptionString = SerializableException.ToBase64String(SerializedException);
			HadException = true;
		}

		public void ResetAndRetainAttemptsCount()
		{
			Base64ExceptionString = null;
			IsReport = false;
			HadException = false;
			SerializedException = null;
		}
	}
}