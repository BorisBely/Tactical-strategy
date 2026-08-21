using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Play debug: Use-of-Force cycle, CQB arena spawn, side task counts, labels over units.
/// Tactical orders live on <see cref="GameCommandInput"/>, not this overlay.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class UseOfForceDebugOverlay : MonoBehaviour
{
	#region Constants
	private const float c_Width = 380f;
	private const float c_ButtonHeight = 34f;
	private const float c_SpawnBoxHeight = 348f;
	private const float c_RoeOnlyBoxHeight = 188f;
	private const float c_LabelHeight = 2.15f;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_HudScratch = new StringBuilder(96);
	private readonly List<UnitTeam> m_Teams = new List<UnitTeam>(64);
	private GUIStyle m_UnitHudStyle;
	private CombatTestArenaSpawner m_Spawner;
	private Camera m_Camera;
	private float m_NextCameraRefreshTime;
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
		CombatTestArenaSpawner spawner = ResolveSpawner();
		float spawnHeight = spawner != null ? c_SpawnBoxHeight : c_RoeOnlyBoxHeight;
		GUI.Box(new Rect(x, y, c_Width, spawnHeight), "Бой (debug)");

		UseOfForceLevel player = UseOfForceSideCommands.Peek(UnitTeamId.Player);
		UseOfForceLevel enemy = UseOfForceSideCommands.Peek(UnitTeamId.Enemy);
		int playerCount = UseOfForceSideCommands.Count(UnitTeamId.Player);
		int enemyCount = UseOfForceSideCommands.Count(UnitTeamId.Enemy);

		if (GUI.Button(new Rect(x + 10f, y + 28f, c_Width - 20f, c_ButtonHeight),
			    "Игрок (сила): " + player + "  [" + playerCount + "]"))
		{
			UseOfForceLevel next = UseOfForceSideCommands.Cycle(UnitTeamId.Player);
			Debug.Log("[UseOfForce] Player units → " + next, this);
		}

		if (GUI.Button(new Rect(x + 10f, y + 66f, c_Width - 20f, c_ButtonHeight),
			    "Враг (сила): " + enemy + "  [" + enemyCount + "]"))
		{
			UseOfForceLevel next = UseOfForceSideCommands.Cycle(UnitTeamId.Enemy);
			Debug.Log("[UseOfForce] Enemy units → " + next, this);
		}

		float countsY;
		if (spawner != null)
		{
			if (GUI.Button(new Rect(x + 10f, y + 110f, c_Width - 20f, c_ButtonHeight),
				    "Спавн игрока  [" + playerCount + "]"))
			{
				int spawned = spawner.SpawnSide(CombatTestSpawnMarker.MarkerSide.Player, true);
				Debug.Log("[ArenaSpawn] Player +" + spawned, this);
			}

			if (GUI.Button(new Rect(x + 10f, y + 148f, c_Width - 20f, c_ButtonHeight),
				    "Спавн врага  [" + enemyCount + "]"))
			{
				int spawned = spawner.SpawnSide(CombatTestSpawnMarker.MarkerSide.Enemy, true);
				Debug.Log("[ArenaSpawn] Enemy +" + spawned, this);
			}

			string autoLabel = spawner.AutoSpawnEnabled
				? "Автоспавн: вкл  " + spawner.AutoSpawnRemaining.ToString("0") + "с"
				: "Автоспавн: выкл";
			if (GUI.Button(new Rect(x + 10f, y + 186f, c_Width - 20f, c_ButtonHeight), autoLabel))
				spawner.SetAutoSpawn(!spawner.AutoSpawnEnabled);

			float half = (c_Width - 24f) * 0.5f;
			if (GUI.Button(new Rect(x + 10f, y + 224f, half - 4f, c_ButtonHeight),
				    "-15с  (" + spawner.AutoSpawnInterval.ToString("0") + "с)"))
				spawner.AdjustAutoSpawnInterval(-CombatTestArenaSpawner.AutoSpawnIntervalStep);

			if (GUI.Button(new Rect(x + 14f + half, y + 224f, half - 4f, c_ButtonHeight), "+15с"))
				spawner.AdjustAutoSpawnInterval(CombatTestArenaSpawner.AutoSpawnIntervalStep);

			countsY = y + 266f;
		}
		else
			countsY = y + 110f;

		GUI.Label(new Rect(x + 10f, countsY, c_Width - 20f, 18f),
			"Игрок [" + playerCount + "]: " + TacticalSideCommands.Describe(UnitTeamId.Player));
		GUI.Label(new Rect(x + 10f, countsY + 18f, c_Width - 20f, 18f),
			"Враг [" + enemyCount + "]: " + TacticalSideCommands.Describe(UnitTeamId.Enemy));
		GUI.Label(new Rect(x + 10f, countsY + 36f, c_Width - 20f, 18f),
			"Нейтрал: " + TacticalSideCommands.Describe(UnitTeamId.Neutral));

		DrawUnitWorldLabels();
	}
	#endregion

	#region Private Methods
	private CombatTestArenaSpawner ResolveSpawner()
	{
		if (m_Spawner != null)
			return m_Spawner;
		m_Spawner = FindAnyObjectByType<CombatTestArenaSpawner>();
		return m_Spawner;
	}

	private Camera ResolveCamera()
	{
		if (m_Camera != null && Time.unscaledTime < m_NextCameraRefreshTime)
			return m_Camera;

		m_Camera = Camera.main;
		m_NextCameraRefreshTime = Time.unscaledTime + 1f;
		return m_Camera;
	}

	private void DrawUnitWorldLabels()
	{
		Camera camera = ResolveCamera();
		if (camera == null)
			return;

		EnsureUnitHudStyle();
		UnitTeam.CopyActive(m_Teams);
		for (int i = 0; i < m_Teams.Count; i++)
		{
			UnitTeam team = m_Teams[i];
			if (team == null)
				continue;
			if (team.TryGetComponent(out UnitHealth health) && health != null && health.IsDead)
				continue;

			Vector3 world = team.transform.position + Vector3.up * c_LabelHeight;
			Vector3 screen = camera.WorldToScreenPoint(world);
			if (screen.z <= 0.15f)
				continue;

			Color prev = GUI.color;
			GUI.color = TeamHudColor(team.Team);
			float gx = screen.x;
			float gy = Screen.height - screen.y;
			GUI.Label(new Rect(gx - 84f, gy - 36f, 168f, 40f), BuildUnitHud(team), m_UnitHudStyle);
			GUI.color = prev;
		}
	}

	private string BuildUnitHud(UnitTeam _team)
	{
		m_HudScratch.Length = 0;
		m_HudScratch.Append(UnitActionLog.Slot(_team));
		if (_team.TryGetComponent(out UnitAIController ai) && ai != null)
		{
			m_HudScratch.Append(' ').Append(ShortState(ai.CurrentState));
			m_HudScratch.Append('/').Append(ai.CurrentAction);
			m_HudScratch.Append('\n');
			m_HudScratch.Append(ai.CurrentNavigationReason);
			if (_team.TryGetComponent(out NavMeshAgent agent) && agent != null && agent.enabled)
			{
				m_HudScratch.Append(" rem=").Append(UnitActionLog.AgentRemaining(agent));
				m_HudScratch.Append(' ').Append(UnitActionLog.AgentPath(agent));
			}
		}
		else
			m_HudScratch.Append(" ai=none");

		return m_HudScratch.ToString();
	}

	private void EnsureUnitHudStyle()
	{
		if (m_UnitHudStyle != null)
			return;

		m_UnitHudStyle = new GUIStyle(GUI.skin.box)
		{
			fontSize = 11,
			alignment = TextAnchor.MiddleCenter,
			wordWrap = true
		};
		m_UnitHudStyle.normal.textColor = Color.white;
	}

	private static Color TeamHudColor(UnitTeamId _team)
	{
		switch (_team)
		{
			case UnitTeamId.Player:
				return new Color(0.45f, 0.85f, 1f, 0.92f);
			case UnitTeamId.Enemy:
				return new Color(1f, 0.55f, 0.35f, 0.92f);
			default:
				return new Color(0.8f, 0.8f, 0.8f, 0.85f);
		}
	}

	private static string ShortState(UnitAIState _state)
	{
		switch (_state)
		{
			case UnitAIState.Defense:
				return "Def";
			case UnitAIState.Attack:
				return "Atk";
			case UnitAIState.Search:
				return "Srch";
			case UnitAIState.Retreat:
				return "Ret";
			case UnitAIState.Flee:
				return "Flee";
			default:
				return "Idle";
		}
	}
	#endregion
}
