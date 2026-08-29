#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Vision.Tests.Editor;

namespace Shooting.Tests.Editor
{
	/// <summary>Phase H EditMode fixtures without Test Runner window selection.</summary>
	public static class WeaponBalanceHNUnitRunner
	{
		private const string c_MenuPath = "Tools/Tests/Archive/Weapon/Run Weapon Balance H-TEST (EditMode)";

		[MenuItem(c_MenuPath, false, 185)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[] { "Shooting.Tests.WeaponBalanceHRunnerTests" }
				},
				new Callbacks());
			Debug.Log(
				"[WeaponBalance H-TEST] EditMode started: WeaponBalanceHRunnerTests (H-TEST-1..11). " +
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
					$"[WeaponBalance H-TEST] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[WeaponBalance H-TEST] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
