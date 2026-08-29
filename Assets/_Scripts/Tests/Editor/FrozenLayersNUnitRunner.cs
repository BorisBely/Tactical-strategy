#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #10 Search 2.0 + #11 Command Priority EditMode regression in one run.
	/// </summary>
	public static class FrozenLayersNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Regression (EditMode)", false, 12)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.Search20Tests",
						"AI.Tests.SearchAttackHoldTests",
						"AI.Tests.UnitAISearchTests",
						"AI.Tests.UnitAISearchExecutionTests",
						"AI.Tests.CommandPriorityTests",
						"AI.Tests.CoverOccupyLifecycleTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[FrozenLayers] EditMode started: Search 2.0 + SearchAttackHold + Command Priority + CoverOccupy. " +
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
					$"[FrozenLayers] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[FrozenLayers] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
