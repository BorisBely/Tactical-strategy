using UnityEngine;

/// <summary>
/// Play debug: two on-screen buttons cycle Use-of-Force for all Player units and all Enemy units.
/// Does not change UnitAIState. Adds <see cref="UnitAIController"/> at runtime if missing.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class UseOfForceDebugOverlay : MonoBehaviour
{
	#region Constants
	private const float c_Width = 360f;
	private const float c_ButtonHeight = 36f;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		if (FindAnyObjectByType<UseOfForceDebugOverlay>() != null)
			return;

		var go = new GameObject("UseOfForceDebugOverlay");
		DontDestroyOnLoad(go);
		go.AddComponent<UseOfForceDebugOverlay>();
	}

	private void OnGUI()
	{
		if (Application.isBatchMode)
			return;
		if (DetectionHarnessPlayMode.IsCalibrationPlay || DetectionHarnessPlayMode.IsGRegressionPlay)
			return;

		float x = Screen.width - c_Width - 12f;
		float y = 12f;
		GUI.Box(new Rect(x, y, c_Width, 108f), "Применение силы (debug)");

		UseOfForceLevel player = UseOfForceSideCommands.Peek(UnitTeamId.Player);
		UseOfForceLevel enemy = UseOfForceSideCommands.Peek(UnitTeamId.Enemy);

		if (GUI.Button(new Rect(x + 10f, y + 28f, c_Width - 20f, c_ButtonHeight),
			    "Игрок (сила): " + player + "  [" + UseOfForceSideCommands.Count(UnitTeamId.Player) + "]"))
		{
			UseOfForceLevel next = UseOfForceSideCommands.Cycle(UnitTeamId.Player);
			Debug.Log("[UseOfForce] Player units → " + next, this);
		}

		if (GUI.Button(new Rect(x + 10f, y + 66f, c_Width - 20f, c_ButtonHeight),
			    "Враг (сила): " + enemy + "  [" + UseOfForceSideCommands.Count(UnitTeamId.Enemy) + "]"))
		{
			UseOfForceLevel next = UseOfForceSideCommands.Cycle(UnitTeamId.Enemy);
			Debug.Log("[UseOfForce] Enemy units → " + next, this);
		}
	}
	#endregion
}
