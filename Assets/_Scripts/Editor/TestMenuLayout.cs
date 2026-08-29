#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Tools/Tests: Current (#13.2) → Regression (#7–#11, 2 пункта) → Archive.
/// </summary>
public static class TestMenuLayout
{
	#region Current
	[MenuItem("Tools/Tests/── Current ──", false, 1)]
	private static void HeaderCurrent() { }

	[MenuItem("Tools/Tests/── Current ──", true)]
	private static bool HeaderCurrentValidate() => false;
	#endregion

	#region Regression
	[MenuItem("Tools/Tests/── Regression ──", false, 10)]
	private static void HeaderRegression() { }

	[MenuItem("Tools/Tests/── Regression ──", true)]
	private static bool HeaderRegressionValidate() => false;
	#endregion
}
#endif
