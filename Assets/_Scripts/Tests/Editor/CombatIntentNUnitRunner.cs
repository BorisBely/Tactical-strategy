#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// Stage 2 FROZEN EditMode fixtures without Test Runner window selection (avoids TestListGUI NRE).
	/// </summary>
	public static class CombatIntentNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Combat Engage Execution (EditMode)", false, 145)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"Vision.Tests.CombatIntentMathTests",
						"Vision.Tests.CombatIntentExecutionTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[CombatIntent] EditMode started: CombatIntentMathTests + CombatIntentExecutionTests. " +
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
					$"[CombatIntent] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[CombatIntent] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
