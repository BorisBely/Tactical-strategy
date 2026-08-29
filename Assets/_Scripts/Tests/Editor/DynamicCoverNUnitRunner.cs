#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #13 Dynamic Cover EditMode (FROZEN 13.0–13.8).
	/// </summary>
	public static class DynamicCoverNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Dynamic Cover (EditMode)", false, 2)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.DynamicCoverCacheTests",
						"AI.Tests.DynamicCoverGenerationTests",
						"AI.Tests.DynamicCoverClassificationTests",
						"AI.Tests.DynamicCoverEvaluationTests",
						"AI.Tests.DynamicCoverEmergencyTests",
						"AI.Tests.DynamicCoverTacticalTests",
						"AI.Tests.DynamicCoverOccupancyTests",
						"AI.Tests.DynamicCoverPeekTests",
						"AI.Tests.DynamicCoverIntegrationTests",
						"AI.Tests.CoverOccupyLifecycleTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[DynamicCover] EditMode started: 13.0–13.8 integration + CoverOccupy. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[DynamicCover] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[DynamicCover] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
