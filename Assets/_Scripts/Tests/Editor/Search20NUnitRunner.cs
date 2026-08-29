#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #10 Search 2.0 EditMode: new A–E fixtures plus frozen Search decision/execution regression.
	/// </summary>
	public static class Search20NUnitRunner
	{
		[MenuItem("Tools/Tests/Archive/Regression/Run Search 2.0 (EditMode)", false, 184)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.Search20Tests",
						"AI.Tests.SearchAttackHoldTests",
						"AI.Tests.UnitAISearchTests",
						"AI.Tests.UnitAISearchExecutionTests"
					}
				},
				new Callbacks());
			Debug.Log(
				"[Search20] EditMode started: Search20Tests + SearchAttackHoldTests + UnitAISearchTests + " +
				"UnitAISearchExecutionTests. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[Search20] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[Search20] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
