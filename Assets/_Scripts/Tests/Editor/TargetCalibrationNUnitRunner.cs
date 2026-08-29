#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #12 Target Calibration EditMode: A–J selection / hysteresis / mission / mismatch.
	/// </summary>
	public static class TargetCalibrationNUnitRunner
	{
		[MenuItem("Tools/Tests/Archive/Regression/Run Target Calibration (EditMode)", false, 188)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"Vision.Tests.TargetCalibrationTests"
					}
				},
				new Callbacks());
			Debug.Log("[TargetCalibration] EditMode started: TargetCalibrationTests. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[TargetCalibration] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[TargetCalibration] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
