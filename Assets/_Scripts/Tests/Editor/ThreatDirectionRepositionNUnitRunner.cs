#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14C.5 Threat Direction → Reposition Decision EditMode.
	/// </summary>
	public static class ThreatDirectionRepositionNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Threat Direction Reposition (EditMode)", false, 16)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ThreatDirectionRepositionTests"
				}
			},
			new Callbacks());
			Debug.Log("[ThreatDirectionReposition] EditMode started: 14C.5. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ThreatDirectionReposition] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ThreatDirectionReposition] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
