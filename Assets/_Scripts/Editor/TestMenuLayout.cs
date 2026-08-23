#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Tools/Tests order: Current (top) → closed vision newest-first → regression.
/// Older calibration / G isolates / frozen tactics live under Tools/Tests/Archive.
/// </summary>
public static class TestMenuLayout
{
	#region Current
	[MenuItem("Tools/Tests/── Current ──", false, 1)]
	private static void HeaderCurrent() { }

	[MenuItem("Tools/Tests/── Current ──", true)]
	private static bool HeaderCurrentValidate() => false;
	#endregion

	#region Closed vision
	[MenuItem("Tools/Tests/── Closed vision ──", false, 20)]
	private static void HeaderClosedVision() { }

	[MenuItem("Tools/Tests/── Closed vision ──", true)]
	private static bool HeaderClosedVisionValidate() => false;
	#endregion

	#region Regression
	[MenuItem("Tools/Tests/── Regression ──", false, 40)]
	private static void HeaderRegression() { }

	[MenuItem("Tools/Tests/── Regression ──", true)]
	private static bool HeaderRegressionValidate() => false;
	#endregion
}
#endif
