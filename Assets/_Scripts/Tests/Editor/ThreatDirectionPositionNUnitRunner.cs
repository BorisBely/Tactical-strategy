#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14C.3 Threat Direction → Tactical Positioning EditMode.
	/// </summary>
	public static class ThreatDirectionPositionNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Threat Direction Position (EditMode)", false, 12)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ThreatDirectionPositionTests"
				}
			},
			new Callbacks());
			Debug.Log("[ThreatDirectionPosition] EditMode started: 14C.3. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ThreatDirectionPosition] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ThreatDirectionPosition] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
