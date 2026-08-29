#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
		/// #14 Tactical Movement EditMode. CLOSED / FROZEN. 14.0–14.10.
	/// </summary>
	public static class TacticalMovementNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Tactical Movement (EditMode)", false, 1)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.TacticalMovementContractTests",
						"AI.Tests.TacticalRouteEvaluationTests",
						"AI.Tests.TacticalCoverToCoverTests",
						"AI.Tests.TacticalUrbanWallBiasTests",
						"AI.Tests.TacticalExposureTraversalTests",
						"AI.Tests.TacticalReplanTests",
						"AI.Tests.TacticalUnderFireTests",
						"AI.Tests.TacticalArrivalTests",
						"AI.Tests.TacticalMovingLeanTests",
						"AI.Tests.TacticalLodTests",
						"AI.Tests.TacticalMovementFinalAcceptanceTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[TacticalMovement] EditMode started: 14.0–14.10 closed / FROZEN. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[TacticalMovement] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[TacticalMovement] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
