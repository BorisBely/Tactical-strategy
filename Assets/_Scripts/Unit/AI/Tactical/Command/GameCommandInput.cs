using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Stage 6.3 input layer. Latches a command, picks a point, issues the same
/// <see cref="TacticalCommand"/> through <see cref="GameCommandService"/> to each recipient.
/// Not Group AI. Not an AI state. This is the Play HUD for orders.
/// </summary>
[DefaultExecutionOrder(201)]
[DisallowMultipleComponent]
public sealed class GameCommandInput : MonoBehaviour
{
	#region Constants
	private const float c_BarHeight = 118f;
	private const float c_ButtonWidth = 72f;
	private const float c_ButtonHeight = 26f;
	private const float c_RayDistance = 2500f;
	private const float c_NavSample = 6f;
	private const float c_MarkerSeconds = 4f;
	private const float c_OverlayReserve = 392f;
	#endregion

	#region Private Fields
	private static GameCommandInput s_Instance;

	private readonly List<Component> m_Recipients = new List<Component>(32);
	private readonly List<GameCommandResult> m_Results = new List<GameCommandResult>(32);

	private GameCommandInputMode m_Mode;
	private GameCommandAudience m_Audience;
	private Rect m_BarRect;
	private Vector3 m_LastPoint;
	private float m_LastPointUntil;
	private bool m_HasLastPoint;
	#endregion

	#region Public Properties
	public static GameCommandInput Instance => s_Instance;

	public GameCommandInputMode Mode => m_Mode;
	public GameCommandAudience Audience => m_Audience;
	public int LastAcceptedCount { get; private set; }
	public string LastSkipReason { get; private set; } = string.Empty;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		if (FindAnyObjectByType<GameCommandInput>() != null)
			return;

		var go = new GameObject("GameCommandInput");
		DontDestroyOnLoad(go);
		go.AddComponent<GameCommandInput>();
	}

	private void Awake()
	{
		s_Instance = this;
	}

	private void OnDestroy()
	{
		ResetToNormal();
		if (s_Instance == this)
			s_Instance = null;
	}

	private void Update()
	{
		if (Application.isBatchMode)
			return;
		if (DetectionHarnessPlayMode.IsCalibrationPlay || DetectionHarnessPlayMode.IsGRegressionPlay)
			return;
		if (PauseMenuController.IsPaused)
			return;
		if (m_Mode == GameCommandInputMode.Normal)
			return;

		bool pickingAtStart = TacticalDebugOrderSession.IsPicking;
		if (m_Audience == GameCommandAudience.PlayerSelected)
			TacticalDebugOrderSession.SetPicking(SelectedPlayerCount() > 0);
		else
			TacticalDebugOrderSession.SetPicking(true);

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			CancelPending();
			return;
		}

		Mouse mouse = Mouse.current;
		if (mouse == null)
			return;

		bool lmb = mouse.leftButton.wasPressedThisFrame;
		bool rmb = mouse.rightButton.wasPressedThisFrame;
		if (!lmb && !rmb)
			return;

		if (lmb && m_Audience == GameCommandAudience.PlayerSelected && !pickingAtStart)
		{
			if (!rmb)
				return;
			lmb = false;
		}

		if (lmb)
			TacticalDebugOrderSession.ConsumeLeftClick();
		if (rmb)
			TacticalDebugOrderSession.ConsumeRightClick();
		if (IsGuiMouseOver())
			return;

		if (!TryResolveWorldPoint(out Vector3 point))
		{
			CancelPending();
			return;
		}

		ConfirmPoint(point);
	}

	private void OnGUI()
	{
		if (Application.isBatchMode)
			return;
		if (DetectionHarnessPlayMode.IsCalibrationPlay || DetectionHarnessPlayMode.IsGRegressionPlay)
			return;
		if (PauseMenuController.IsPaused)
			return;

		float width = Mathf.Max(420f, Screen.width - c_OverlayReserve - 24f);
		m_BarRect = new Rect(12f, Screen.height - c_BarHeight - 12f, width, c_BarHeight);
		GUI.Box(m_BarRect, "Game commands");

		float x = m_BarRect.x + 10f;
		float y = m_BarRect.y + 22f;
		GUI.Label(new Rect(x, y, 56f, c_ButtonHeight), "Player");
		x += 58f;
		DrawModeButton(ref x, y, "Attack", GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
		DrawModeButton(ref x, y, "Defense", GameCommandInputMode.DefensePending, GameCommandAudience.PlayerSelected);
		DrawModeButton(ref x, y, "Retreat", GameCommandInputMode.RetreatPending, GameCommandAudience.PlayerSelected);
		DrawModeButton(ref x, y, "Search", GameCommandInputMode.SearchPending, GameCommandAudience.PlayerSelected);
		DrawModeButton(ref x, y, "Flee", GameCommandInputMode.FleePending, GameCommandAudience.PlayerSelected);
		if (GUI.Button(new Rect(x, y, c_ButtonWidth, c_ButtonHeight), "Cancel"))
			IssueImmediateCancel(GameCommandAudience.PlayerSelected);

		x = m_BarRect.x + 10f;
		y += c_ButtonHeight + 4f;
		GUI.Label(new Rect(x, y, 88f, c_ButtonHeight), "Enemy Debug");
		x += 90f;
		DrawModeButton(ref x, y, "Attack", GameCommandInputMode.AttackPending, GameCommandAudience.EnemyDebug);
		DrawModeButton(ref x, y, "Defense", GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
		DrawModeButton(ref x, y, "Retreat", GameCommandInputMode.RetreatPending, GameCommandAudience.EnemyDebug);
		DrawModeButton(ref x, y, "Search", GameCommandInputMode.SearchPending, GameCommandAudience.EnemyDebug);
		DrawModeButton(ref x, y, "Flee", GameCommandInputMode.FleePending, GameCommandAudience.EnemyDebug);
		if (GUI.Button(new Rect(x, y, c_ButtonWidth, c_ButtonHeight), "Cancel"))
			IssueImmediateCancel(GameCommandAudience.EnemyDebug);

		string selected = "выбрано " + SelectedPlayerCount();
		string last = string.IsNullOrEmpty(LastSkipReason)
			? "ок=" + LastAcceptedCount
			: "пропуск=" + LastSkipReason;
		string hint = m_Mode == GameCommandInputMode.Normal
			? selected + "  |  " + last + "  |  приказ, затем ЛКМ или ПКМ по точке. Esc отмена."
			: ModeLabel(m_Mode) + " / " + AudienceLabel(m_Audience) + " — " + selected +
			  " — ЛКМ или ПКМ по точке. Esc отмена.";
		if (m_Mode != GameCommandInputMode.Normal &&
		    m_Audience == GameCommandAudience.PlayerSelected &&
		    SelectedPlayerCount() == 0)
			hint = "Нет выбранных — выделите юнитов ЛКМ, затем ЛКМ или ПКМ по точке.";
		GUI.Label(new Rect(m_BarRect.x + 10f, m_BarRect.yMax - 22f, m_BarRect.width - 20f, 20f), hint);

		DrawHoverCross();
		DrawLastPointMarker();
	}
	#endregion

	#region Public Methods
	public void BeginPending(GameCommandInputMode _mode, GameCommandAudience _audience)
	{
		if (_mode == GameCommandInputMode.Normal)
		{
			CancelPending();
			return;
		}

		m_Mode = _mode;
		m_Audience = _audience;
		LastAcceptedCount = 0;
		LastSkipReason = string.Empty;
		TacticalDebugOrderSession.SetCommandPointPending(true);
		TacticalDebugOrderSession.SetPicking(
			_audience != GameCommandAudience.PlayerSelected || SelectedPlayerCount() > 0);
		Log("pending", 0, false, default);
	}

	public int ConfirmPoint(Vector3 _point)
	{
		return ConfirmPoint(_point, null);
	}

	public int ConfirmPoint(Vector3 _point, IReadOnlyList<Component> _recipients)
	{
		if (m_Mode == GameCommandInputMode.Normal)
		{
			Skip("NotPending", 0, false, default);
			return 0;
		}

		if (!TryBuildCommand(_point, out TacticalCommand command))
		{
			Skip("NotPending", 0, false, default);
			ResetToNormal();
			return 0;
		}

		ResolveRecipients(_recipients);
		if (m_Recipients.Count == 0)
		{
			Skip("NoRecipients", 0, true, _point);
			ShowMarker(_point);
			ResetToNormal();
			return 0;
		}

		if (m_Audience == GameCommandAudience.EnemyDebug)
			GameCommandRecipientQuery.EnsureEnemyDebugReceivers(m_Recipients);

		Log("issue", m_Recipients.Count, true, _point);
		m_Results.Clear();
		int accepted = GameCommandService.IssueMany(m_Recipients, in command, m_Results);
		LastAcceptedCount = accepted;
		LastSkipReason = accepted > 0 ? string.Empty : FirstRejectReason(m_Results);
		ShowMarker(_point);
		ResetToNormal();
		return accepted;
	}

	public int CancelPending()
	{
		if (m_Mode == GameCommandInputMode.Normal)
		{
			LastAcceptedCount = 0;
			LastSkipReason = string.Empty;
			return 0;
		}

		LastAcceptedCount = 0;
		LastSkipReason = string.Empty;
		Log("cancel", 0, false, default);
		ResetToNormal();
		return 0;
	}

	public int IssueImmediateCancel(GameCommandAudience _audience)
	{
		m_Audience = _audience;
		m_Mode = GameCommandInputMode.Normal;
		TacticalDebugOrderSession.SetCommandPointPending(false);
		TacticalDebugOrderSession.SetPicking(false);

		GameCommandRecipientQuery.Collect(_audience, m_Recipients);
		if (m_Recipients.Count == 0)
		{
			Skip("NoRecipients", 0, false, default);
			return 0;
		}

		if (_audience == GameCommandAudience.EnemyDebug)
			GameCommandRecipientQuery.EnsureEnemyDebugReceivers(m_Recipients);

		TacticalCommand command = TacticalCommand.Cancel(TacticalCommandSource.Game);
		Log("issue", m_Recipients.Count, false, default);
		m_Results.Clear();
		int accepted = GameCommandService.IssueMany(m_Recipients, in command, m_Results);
		LastAcceptedCount = accepted;
		LastSkipReason = string.Empty;
		return accepted;
	}
	#endregion

	#region Private Methods
	private void DrawModeButton(
		ref float _x,
		float _y,
		string _label,
		GameCommandInputMode _mode,
		GameCommandAudience _audience)
	{
		bool active = m_Mode == _mode && m_Audience == _audience;
		Color prev = GUI.backgroundColor;
		if (active)
			GUI.backgroundColor = new Color(1f, 0.72f, 0.2f, 1f);

		if (GUI.Button(new Rect(_x, _y, c_ButtonWidth, c_ButtonHeight), _label))
			BeginPending(_mode, _audience);

		GUI.backgroundColor = prev;
		_x += c_ButtonWidth + 4f;
	}

	private void DrawHoverCross()
	{
		if (m_Mode == GameCommandInputMode.Normal || Mouse.current == null)
			return;

		Vector2 mouse = Mouse.current.position.ReadValue();
		Vector2 gui = new Vector2(mouse.x, Screen.height - mouse.y);
		const float arm = 8f;
		Color prev = GUI.color;
		GUI.color = new Color(1f, 0.85f, 0.2f, 0.95f);
		GUI.DrawTexture(new Rect(gui.x - arm, gui.y - 1f, arm * 2f, 2f), Texture2D.whiteTexture);
		GUI.DrawTexture(new Rect(gui.x - 1f, gui.y - arm, 2f, arm * 2f), Texture2D.whiteTexture);
		GUI.color = prev;
	}

	private void DrawLastPointMarker()
	{
		if (!m_HasLastPoint || Time.unscaledTime > m_LastPointUntil)
			return;

		Camera camera = ResolveCamera();
		if (camera == null)
			return;

		Vector3 screen = camera.WorldToScreenPoint(m_LastPoint);
		if (screen.z <= 0f)
			return;

		Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
		Color prev = GUI.color;
		GUI.color = new Color(1f, 0.45f, 0.08f, 0.95f);
		GUI.Box(new Rect(gui.x - 11f, gui.y - 11f, 22f, 22f), GUIContent.none);
		GUI.DrawTexture(new Rect(gui.x - 14f, gui.y - 1.5f, 28f, 3f), Texture2D.whiteTexture);
		GUI.DrawTexture(new Rect(gui.x - 1.5f, gui.y - 14f, 3f, 28f), Texture2D.whiteTexture);
		GUI.color = prev;
	}

	private bool IsGuiMouseOver()
	{
		Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
		Vector2 gui = new Vector2(mouse.x, Screen.height - mouse.y);
		return m_BarRect.Contains(gui);
	}

	private static bool TryResolveWorldPoint(out Vector3 _point)
	{
		_point = default;
		Camera camera = ResolveCamera();
		if (camera == null || Mouse.current == null)
			return false;

		Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, c_RayDistance, ~0, QueryTriggerInteraction.Ignore))
			return false;

		if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, c_NavSample, NavMesh.AllAreas))
			_point = navHit.position;
		else
			_point = hit.point;

		return true;
	}

	private static Camera ResolveCamera()
	{
		if (Camera.main != null)
			return Camera.main;

		Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
		return cameras != null && cameras.Length > 0 ? cameras[0] : null;
	}

	private bool TryBuildCommand(Vector3 _point, out TacticalCommand _command)
	{
		TacticalCommandSource source = TacticalCommandSource.Game;
		switch (m_Mode)
		{
			case GameCommandInputMode.AttackPending:
				_command = TacticalCommand.Attack(_point, null, source);
				return true;
			case GameCommandInputMode.DefensePending:
				_command = TacticalCommand.Defense(_point, source);
				return true;
			case GameCommandInputMode.RetreatPending:
				_command = TacticalCommand.Retreat(_point, source);
				return true;
			case GameCommandInputMode.SearchPending:
				_command = TacticalCommand.Search(_point, source);
				return true;
			case GameCommandInputMode.FleePending:
				_command = TacticalCommand.Flee(_point, source);
				return true;
			default:
				_command = default;
				return false;
		}
	}

	private void ResolveRecipients(IReadOnlyList<Component> _override)
	{
		if (_override != null)
		{
			m_Recipients.Clear();
			for (int i = 0; i < _override.Count; i++)
			{
				if (_override[i] != null)
					m_Recipients.Add(_override[i]);
			}

			return;
		}

		GameCommandRecipientQuery.Collect(m_Audience, m_Recipients);
	}

	private void ResetToNormal()
	{
		m_Mode = GameCommandInputMode.Normal;
		TacticalDebugOrderSession.SetCommandPointPending(false);
		TacticalDebugOrderSession.SetPicking(false);
	}

	private void ShowMarker(Vector3 _point)
	{
		m_LastPoint = _point;
		m_HasLastPoint = true;
		m_LastPointUntil = Time.unscaledTime + c_MarkerSeconds;
	}

	private static int SelectedPlayerCount()
	{
		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		return selection != null ? selection.SelectedUnitCount : 0;
	}

	private static string FirstRejectReason(List<GameCommandResult> _results)
	{
		if (_results == null)
			return "NoneAccepted";
		for (int i = 0; i < _results.Count; i++)
		{
			if (!_results[i].Accepted)
				return _results[i].Reason.ToString();
		}

		return "NoneAccepted";
	}

	private static string AudienceLabel(GameCommandAudience _audience)
	{
		return _audience == GameCommandAudience.EnemyDebug ? "враг" : "игрок";
	}

	private void Skip(string _reason, int _n, bool _hasPos, Vector3 _pos)
	{
		LastAcceptedCount = 0;
		LastSkipReason = _reason;
		Log("skip", _n, _hasPos, _pos, _reason);
	}

	private void Log(string _verb, int _n, bool _hasPos, Vector3 _pos, string _skip = null)
	{
		if (!UnitActionLog.Enabled)
			return;

		string payload =
			"mode=" + m_Mode +
			" audience=" + m_Audience +
			" verb=" + _verb +
			" n=" + _n;
		if (_hasPos)
			payload += " pos=" + UnitActionLog.Vec(_pos);
		if (!string.IsNullOrEmpty(_skip))
			payload += " skip=" + _skip;
		if (_verb == "issue" && m_Recipients.Count > 0)
			payload += " units=" + FormatRecipientSlots();
		UnitActionLog.Timeline(UnitActionLog.Input, payload);
	}

	private string FormatRecipientSlots()
	{
		string slots = string.Empty;
		for (int i = 0; i < m_Recipients.Count; i++)
		{
			if (i > 0)
				slots += ",";
			slots += m_Recipients[i] != null ? UnitActionLog.Slot(m_Recipients[i]) : "none";
		}

		return slots;
	}

	private static string ModeLabel(GameCommandInputMode _mode)
	{
		switch (_mode)
		{
			case GameCommandInputMode.AttackPending:
				return "Attack";
			case GameCommandInputMode.DefensePending:
				return "Defense";
			case GameCommandInputMode.RetreatPending:
				return "Retreat";
			case GameCommandInputMode.SearchPending:
				return "Search";
			case GameCommandInputMode.FleePending:
				return "Flee";
			default:
				return "Normal";
		}
	}
	#endregion
}
