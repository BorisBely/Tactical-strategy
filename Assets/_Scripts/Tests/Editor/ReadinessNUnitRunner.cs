#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14B Readiness EditMode. 14B.0–14B.7.
	/// </summary>
	public static class ReadinessNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Readiness (EditMode)", false, 4)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ReadinessContractTests",
					"AI.Tests.ReadinessStimulusTests",
					"AI.Tests.ReadinessPoseTests",
					"AI.Tests.ReadinessIntegrationTests",
					"AI.Tests.ReadinessBalanceTests",
					"AI.Tests.ReadinessPersistenceTests",
					"AI.Tests.ReadinessFatigueTests",
					"AI.Tests.ReadinessCombatIntegrationTests"
				}
			},
			new Callbacks());
			Debug.Log("[Readiness] EditMode started: 14B.0–14B.7. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[Readiness] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[Readiness] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
