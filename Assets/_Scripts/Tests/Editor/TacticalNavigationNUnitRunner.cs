#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// Stage 4 FROZEN tactical navigation EditMode fixtures without Test Runner window selection.
	/// </summary>
	public static class TacticalNavigationNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Tactical Navigation (EditMode)", false, 143)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.UnitAITacticalNavigationTests",
						"AI.Tests.UnitAISearchExecutionTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[TacticalNavigation] EditMode started: UnitAITacticalNavigationTests + UnitAISearchExecutionTests. " +
				"Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[TacticalNavigation] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[TacticalNavigation] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
