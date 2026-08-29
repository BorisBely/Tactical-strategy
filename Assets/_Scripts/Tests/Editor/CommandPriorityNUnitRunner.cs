#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// #11 Command Priority EditMode: A–I replacement / interrupt / ImmediateThreat / determinism.
	/// </summary>
	public static class CommandPriorityNUnitRunner
	{
		[MenuItem("Tools/Tests/Archive/Regression/Run Command Priority (EditMode)", false, 186)]
		public static void RunFromMenu()
		{
			EditModeNUnitRun.Execute(
				new Filter
				{
					testMode = TestMode.EditMode,
					groupNames = new[]
					{
						"AI.Tests.CommandPriorityTests"
					}
				},
				new Callbacks());
			Debug.Log("[CommandPriority] EditMode started: CommandPriorityTests. Wait for the finished log.");
		}

		private sealed class Callbacks : ICallbacks
		{
			public void RunStarted(ITestAdaptor _)
			{
			}

			public void RunFinished(ITestResultAdaptor result)
			{
				Debug.Log(
					$"[CommandPriority] finished passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
				EditModeNUnitRun.Release(this);
			}

			public void TestStarted(ITestAdaptor _)
			{
			}

			public void TestFinished(ITestResultAdaptor result)
			{
				if (result.HasChildren || result.TestStatus != TestStatus.Failed)
					return;

				Debug.LogError($"[CommandPriority] FAIL {result.Test.FullName}: {result.Message}");
			}
		}
	}
}
#endif
