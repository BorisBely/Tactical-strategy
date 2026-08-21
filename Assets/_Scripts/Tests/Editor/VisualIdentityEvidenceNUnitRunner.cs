#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// Stage 1 EditMode fixtures without Test Runner window selection (avoids TestListGUI NRE).
	/// </summary>
	public static class VisualIdentityEvidenceNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Identity World Evidence (EditMode)", false, 133)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"Vision.Tests.VisualAffiliationMappingTests",
						"Vision.Tests.VisualIdentityEvidenceTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[IdentityWorldEvidence] EditMode started: VisualAffiliationMappingTests + VisualIdentityEvidenceTests. " +
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
					$"[IdentityWorldEvidence] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[IdentityWorldEvidence] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
