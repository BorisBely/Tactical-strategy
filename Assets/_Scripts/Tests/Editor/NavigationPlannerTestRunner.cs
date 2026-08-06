#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VehicleNavigation.Tests.Editor
{
	public static class NavigationPlannerTestRunner
	{
		public static void RunFromCommandLine()
		{
			RunInternal(true);
		}

		[MenuItem("Tools/Tests/Run NavigationPlannerTests")]
		public static void RunFromMenu()
		{
			RunInternal(false);
		}

		static void RunInternal(bool _exitWhenDone)
		{
			var api = ScriptableObject.CreateInstance<TestRunnerApi>();
			var settings = new ExecutionSettings(new Filter
			{
				testMode = TestMode.EditMode,
				assemblyNames = new[] { "_Scripts.Tests" }
			});
			api.RegisterCallbacks(new TestCallbacks(_exitWhenDone));
			api.Execute(settings);
		}

		sealed class TestCallbacks : ICallbacks
		{
			readonly bool m_ExitWhenDone;

			public TestCallbacks(bool _exitWhenDone)
			{
				m_ExitWhenDone = _exitWhenDone;
			}

			public void RunStarted(ITestAdaptor _tests)
			{
				Debug.Log("[NavTests] NavigationPlannerTests started");
			}

			public void RunFinished(ITestResultAdaptor _result)
			{
				Debug.Log(
					$"[NavTests] Finished passed={_result.PassCount} failed={_result.FailCount} skipped={_result.SkipCount}");
				if (m_ExitWhenDone)
					EditorApplication.Exit(_result.FailCount > 0 ? 1 : 0);
			}

			public void TestStarted(ITestAdaptor _test) { }

			public void TestFinished(ITestResultAdaptor _result)
			{
				if (_result.TestStatus == TestStatus.Failed)
				{
					Debug.LogError(
						$"[NavTests] FAIL {_result.Test.FullName}: {_result.Message}\n{_result.Output}");
				}
			}
		}
	}
}
#endif
