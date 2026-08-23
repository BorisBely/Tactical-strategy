#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// Stage 3 FROZEN Search locomotion EditMode fixtures without Test Runner window selection.
	/// </summary>
	public static class SearchExecutionNUnitRunner
	{
		[MenuItem("Tools/Tests/Archive/EditMode/Run Search Execution (EditMode)", false, 141)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.UnitAISearchTests",
						"AI.Tests.UnitAISearchExecutionTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[SearchExecution] EditMode started: UnitAISearchTests + UnitAISearchExecutionTests. " +
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
					$"[SearchExecution] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[SearchExecution] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
