#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14C.4 Dynamic Threat Reorientation EditMode.
	/// </summary>
	public static class ThreatDirectionReorientationNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Threat Direction Reorientation (EditMode)", false, 14)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ThreatDirectionReorientationTests"
				}
			},
			new Callbacks());
			Debug.Log("[ThreatDirectionReorientation] EditMode started: 14C.4. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ThreatDirectionReorientation] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ThreatDirectionReorientation] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
