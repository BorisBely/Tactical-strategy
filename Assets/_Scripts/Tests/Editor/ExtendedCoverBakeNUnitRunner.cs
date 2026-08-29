#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #13.2B Extended Cover Position Bake EditMode.
	/// </summary>
	public static class ExtendedCoverBakeNUnitRunner
	{
		[MenuItem("Tools/Tests/Run Extended Cover Bake (EditMode)", false, 3)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.ExtendedCoverBakeTests"
					}
				},
				new Callbacks());
			Debug.Log("[ExtendedCoverBake] EditMode started: 13.2B.0–13.2B.5A. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[ExtendedCoverBake] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[ExtendedCoverBake] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
