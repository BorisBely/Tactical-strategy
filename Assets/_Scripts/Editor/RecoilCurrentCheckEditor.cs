#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Единственная «текущая» кнопка прогона. Новые тесты/прогоны вешать СЮДА,
/// не новой строкой в Tools/Tests — иначе пункт приходится искать.
/// Замороженные фазы остаются своими пунктами (A Auto, RecoilContract).
/// </summary>
public static class RecoilCurrentCheckEditor
{
	#region Constants
	/// <summary>Менять эту строку вместе с телом RunCurrentFromMenu, когда меняется активный прогон.</summary>
	private const string c_CurrentCheckName = "F2 Benelli pellet spread replay (RecoilPlayF2BenelliSpread_LAST.txt)";
	private const string c_MenuPath = "Tools/Tests/▶ Current Recoil Check";
	#endregion

	#region Menu
	[MenuItem(c_MenuPath, false, 1)]
	private static void RunCurrentFromMenu()
	{
		Debug.Log("[CURRENT] " + c_CurrentCheckName + " — не ищите другие пункты Tools/Tests для этого шага.");
		RecoilPlayBaselineReportEditor.RunF2BenelliSpreadFromMenu();
	}
	#endregion
}
#endif
