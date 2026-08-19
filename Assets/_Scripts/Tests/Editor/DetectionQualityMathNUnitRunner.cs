#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	public static class DetectionQualityMathNUnitRunner
	{
		[MenuItem("Tools/Tests/Run DetectionG1 EditMode NUnit")]
		public static void RunFromMenu()
		{
			var api = ScriptableObject.CreateInstance<TestRunnerApi>();
			var settings = new ExecutionSettings(new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[] { "Vision.Tests" }
			});
			api.RegisterCallbacks(new Callbacks());
			api.Execute(settings);
		}

		sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _) { }

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[DetectionG1 NUnit] finished passed={result.PassCount} failed={result.FailCount}");
			}

			public void TestStarted(ITestAdaptor _) { }

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.TestStatus == TestStatus.Failed)
					Debug.LogError($"[DetectionG1 NUnit] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
