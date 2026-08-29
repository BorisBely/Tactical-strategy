#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #14C Threat Direction Knowledge EditMode.
	/// </summary>
	public static class ThreatDirectionNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Threat Direction (EditMode)", false, 6)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
			new Filter
			{
				testMode = TestMode.EditMode,
				groupNames = new[]
				{
					"AI.Tests.ThreatDirectionTests"
				}
			},
			new Callbacks());
			Debug.Log("[ThreatDirection] EditMode started: 14C. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ThreatDirection] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ThreatDirection] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
