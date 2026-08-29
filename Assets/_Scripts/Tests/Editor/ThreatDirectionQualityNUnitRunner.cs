#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14C.2 Threat Direction confidence / uncertainty EditMode.
	/// </summary>
	public static class ThreatDirectionQualityNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Threat Direction Quality (EditMode)", false, 10)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ThreatDirectionQualityTests"
				}
			},
			new Callbacks());
			Debug.Log("[ThreatDirectionQuality] EditMode started: 14C.2. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ThreatDirectionQuality] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ThreatDirectionQuality] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
