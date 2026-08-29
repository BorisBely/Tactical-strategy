using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.1–13.8 editor/play debug: region, candidate, type, score, occupancy, peek / lean.
/// </summary>
[DisallowMultipleComponent]
public sealed class CoverCandidateDebugDraw : MonoBehaviour
{
	#region Nested
	private struct OccupancySample
	{
		public int CandidateId;
		public CoverOccupancy State;
		public int OwnerUnitId;
		public float TtlRemaining;
	}
	#endregion
	#region Serialized
	[SerializeField] private bool m_DrawInPlay = true;
	[SerializeField] private Color m_RegionColor = new Color(1f, 0.92f, 0.2f, 1f);
	[SerializeField] private Color m_GeneratedColor = new Color(0.2f, 0.85f, 1f, 1f);
	[SerializeField] private Color m_CachedColor = new Color(0.25f, 0.9f, 0.35f, 1f);
	[SerializeField] private Color m_RejectedColor = new Color(1f, 0.25f, 0.2f, 1f);
	[SerializeField] private Color m_NoneColor = new Color(0.55f, 0.55f, 0.55f, 1f);
	[SerializeField] private Color m_CrouchColor = new Color(1f, 0.85f, 0.2f, 1f);
	[SerializeField] private Color m_StandingColor = new Color(0.2f, 0.85f, 1f, 1f);
	[SerializeField] private Color m_PartialColor = new Color(0.95f, 0.35f, 0.9f, 1f);
	[SerializeField] private Color m_CornerColor = new Color(1f, 0.55f, 0.15f, 1f);
	#endregion

	#region Private Fields
	private readonly List<CoverCandidate> m_Candidates = new List<CoverCandidate>(16);
	private readonly List<CoverRejectedSample> m_Rejected = new List<CoverRejectedSample>(32);
	private readonly List<CoverPositionEvaluation> m_Evaluations = new List<CoverPositionEvaluation>(16);
	private readonly List<OccupancySample> m_Occupancy = new List<OccupancySample>(16);
	private Bounds m_RegionBounds;
	private bool m_FromCache;
	private bool m_HasCapture;
	private int m_SelectedId = -1;
	private bool m_EmergencyActive;
	private EmergencyCoverResult m_EmergencyResult;
	private bool m_TacticalActive;
	private TacticalCoverDecisionKind m_TacticalKind;
	private int m_CurrentId = -1;
	private float m_TacticalCurrentScore;
	private float m_TacticalBestScore;
	private float m_TacticalSwitchCost;
	private bool m_PeekActive;
	private CoverPeekDecision m_PeekDecision;
	#endregion

	#region Public Properties
	public bool FromCache => m_FromCache;
	public int CandidateCount => m_Candidates.Count;
	public int EvaluationCount => m_Evaluations.Count;
	public int SelectedId => m_SelectedId;
	public int CurrentId => m_CurrentId;
	public bool EmergencyCoverActive => m_EmergencyActive;
	public EmergencyCoverResult EmergencyResult => m_EmergencyResult;
	public bool TacticalCoverActive => m_TacticalActive;
	public TacticalCoverDecisionKind TacticalDecision => m_TacticalKind;
	public int OccupancySampleCount => m_Occupancy.Count;
	public bool PeekActive => m_PeekActive;
	public CoverPeekDecision PeekDecision => m_PeekDecision;
	public CoverPeekDebugSnapshot PeekSnapshot => m_PeekDecision.Snapshot;
	public CoverPeekDirection PeekDirection => m_PeekDecision.Direction;
	public CoverLeanLevel PeekDepth => m_PeekDecision.Depth;
	#endregion

	#region Public Methods
	public void Capture(
		Bounds _regionBounds,
		IReadOnlyList<CoverCandidate> _candidates,
		bool _fromCache,
		IReadOnlyList<CoverRejectedSample> _rejected)
	{
		CaptureEvaluations(_regionBounds, _candidates, null, -1, _fromCache, _rejected);
	}

	public void CaptureEvaluations(
		Bounds _regionBounds,
		IReadOnlyList<CoverCandidate> _candidates,
		IReadOnlyList<CoverPositionEvaluation> _evaluations,
		int _selectedId,
		bool _fromCache,
		IReadOnlyList<CoverRejectedSample> _rejected)
	{
		m_RegionBounds = _regionBounds;
		m_FromCache = _fromCache;
		m_HasCapture = true;
		m_SelectedId = _selectedId;
		m_Evaluations.Clear();
		m_EmergencyActive = false;
		m_EmergencyResult = EmergencyCoverResult.None;
		m_TacticalActive = false;
		m_TacticalKind = TacticalCoverDecisionKind.None;
		m_CurrentId = -1;
		if (_evaluations != null)
		{
			for (int i = 0; i < _evaluations.Count; i++)
				m_Evaluations.Add(_evaluations[i]);
		}

		m_Candidates.Clear();
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null)
					m_Candidates.Add(_candidates[i]);
			}
		}

		m_Rejected.Clear();
		if (_rejected == null)
			return;
		for (int i = 0; i < _rejected.Count; i++)
			m_Rejected.Add(_rejected[i]);
	}

	public void CaptureEmergency(
		Bounds _regionBounds,
		IReadOnlyList<CoverCandidate> _candidates,
		IReadOnlyList<CoverEmergencyEvaluation> _evaluations,
		EmergencyCoverResult _result,
		int _selectedId,
		bool _fromCache,
		bool _active)
	{
		m_RegionBounds = _regionBounds;
		m_FromCache = _fromCache;
		m_HasCapture = true;
		m_SelectedId = _selectedId;
		m_EmergencyActive = _active;
		m_EmergencyResult = _result;
		m_TacticalActive = false;
		m_TacticalKind = TacticalCoverDecisionKind.None;
		m_Evaluations.Clear();
		if (_evaluations != null)
		{
			for (int i = 0; i < _evaluations.Count; i++)
			{
				CoverEmergencyEvaluation evaluation = _evaluations[i];
				m_Evaluations.Add(new CoverPositionEvaluation
				{
					Candidate = evaluation.Candidate,
					Score = evaluation.Score,
					Valid = evaluation.Valid && evaluation.Acceptable
				});
			}
		}

		m_Candidates.Clear();
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null)
					m_Candidates.Add(_candidates[i]);
			}
		}

		m_Rejected.Clear();
		if (_evaluations == null)
			return;
		for (int i = 0; i < _evaluations.Count; i++)
		{
			CoverEmergencyEvaluation evaluation = _evaluations[i];
			if (evaluation.Candidate == null || evaluation.Acceptable || !evaluation.Valid)
				continue;
			m_Rejected.Add(new CoverRejectedSample
			{
				Position = evaluation.Candidate.Position,
				Normal = evaluation.Candidate.Normal,
				Reason = CoverRejectReason.BelowThreshold
			});
		}
	}

	public void CaptureTactical(
		Bounds _regionBounds,
		IReadOnlyList<CoverCandidate> _candidates,
		IReadOnlyList<CoverPositionEvaluation> _evaluations,
		int _currentId,
		int _bestId,
		float _currentScore,
		float _bestScore,
		float _switchCost,
		TacticalCoverDecisionKind _decision,
		bool _fromCache)
	{
		m_RegionBounds = _regionBounds;
		m_FromCache = _fromCache;
		m_HasCapture = true;
		m_SelectedId = _bestId;
		m_CurrentId = _currentId;
		m_EmergencyActive = false;
		m_EmergencyResult = EmergencyCoverResult.None;
		m_TacticalActive = true;
		m_TacticalKind = _decision;
		m_TacticalCurrentScore = _currentScore;
		m_TacticalBestScore = _bestScore;
		m_TacticalSwitchCost = _switchCost;
		m_Evaluations.Clear();
		if (_evaluations != null)
		{
			for (int i = 0; i < _evaluations.Count; i++)
				m_Evaluations.Add(_evaluations[i]);
		}

		m_Candidates.Clear();
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null)
					m_Candidates.Add(_candidates[i]);
			}
		}

		m_Rejected.Clear();
	}

	public void CaptureOccupancy(
		IReadOnlyList<CoverCandidate> _candidates,
		CoverOccupancyBoard _board,
		float _now)
	{
		m_Occupancy.Clear();
		if (_board == null)
			return;
		m_HasCapture = true;
		if (_candidates != null && m_Candidates.Count == 0)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null)
					m_Candidates.Add(_candidates[i]);
			}
		}

		IReadOnlyList<CoverCandidate> source = _candidates ?? m_Candidates;
		if (source == null)
			return;
		for (int i = 0; i < source.Count; i++)
		{
			CoverCandidate candidate = source[i];
			if (candidate == null)
				continue;
			CoverReservation reservation;
			bool held = _board.TryGetReservation(candidate, _now, out reservation);
			m_Occupancy.Add(new OccupancySample
			{
				CandidateId = candidate.CandidateId,
				State = held ? reservation.State : CoverOccupancy.Available,
				OwnerUnitId = held ? reservation.UnitId : 0,
				TtlRemaining = held && reservation.State == CoverOccupancy.Reserved
					? Mathf.Max(0f, reservation.ExpiresAt - _now)
					: 0f
			});
		}
	}

	public void CapturePeek(in CoverPeekDecision _decision)
	{
		m_HasCapture = true;
		m_PeekActive = true;
		m_PeekDecision = _decision;
	}

	public void CaptureIntegration(
		Bounds _regionBounds,
		IReadOnlyList<CoverCandidate> _candidates,
		IReadOnlyList<CoverPositionEvaluation> _evaluations,
		int _currentId,
		int _bestId,
		float _currentScore,
		float _bestScore,
		float _switchCost,
		TacticalCoverDecisionKind _tactical,
		CoverOccupancyBoard _board,
		float _now,
		in CoverPeekDecision _peek)
	{
		CaptureTactical(
			_regionBounds,
			_candidates,
			_evaluations,
			_currentId,
			_bestId,
			_currentScore,
			_bestScore,
			_switchCost,
			_tactical,
			false);
		CaptureOccupancy(_candidates, _board, _now);
		CapturePeek(in _peek);
	}

	public bool TryGetOccupancyLabel(int _candidateId, out string _label)
	{
		_label = string.Empty;
		for (int i = 0; i < m_Occupancy.Count; i++)
		{
			OccupancySample sample = m_Occupancy[i];
			if (sample.CandidateId != _candidateId)
				continue;
			_label = FormatOccupancy(in sample);
			return true;
		}

		return false;
	}

	public void ClearCapture()
	{
		m_HasCapture = false;
		m_Candidates.Clear();
		m_Rejected.Clear();
		m_Evaluations.Clear();
		m_SelectedId = -1;
		m_EmergencyActive = false;
		m_EmergencyResult = EmergencyCoverResult.None;
		m_TacticalActive = false;
		m_TacticalKind = TacticalCoverDecisionKind.None;
		m_CurrentId = -1;
		m_Occupancy.Clear();
		m_PeekActive = false;
		m_PeekDecision = default;
	}
	#endregion

	#region Unity Lifecycle
	private void OnDrawGizmos()
	{
		DrawGizmos();
	}

	private void Update()
	{
		if (!m_DrawInPlay || !Application.isPlaying || !m_HasCapture)
			return;
		DrawRuntime();
	}
	#endregion

	#region Private Methods
	private void DrawGizmos()
	{
		if (!m_HasCapture)
			return;

		Gizmos.color = m_RegionColor;
		Gizmos.DrawWireCube(m_RegionBounds.center, m_RegionBounds.size);

		for (int i = 0; i < m_Candidates.Count; i++)
		{
			CoverCandidate candidate = m_Candidates[i];
			Vector3 pos = candidate.Position + Vector3.up * 0.05f;
			Color color = ColorForType(candidate.CoverType);
			if (!CoverClassifier.IsTacticalType(candidate.CoverType))
				color.a *= 0.45f;
			Gizmos.color = color;
			Gizmos.DrawSphere(pos, 0.12f);
			CoverTypeVisual.DrawGeometryAxes(
				candidate.Position,
				candidate.Normal,
				candidate.EdgeDirection,
				candidate.OpeningWidth,
				candidate.OpeningAxis,
				candidate.OpeningCenter,
				candidate.WindowValid,
				candidate.WindowCenter,
				candidate.WindowAxis,
				candidate.WindowWidth,
				candidate.HasFrame,
				candidate.HasTransparentPane,
				candidate.CornerFacing,
				candidate.CornerNormalA,
				candidate.CornerNormalB);
		}

		if (m_TacticalActive)
		{
			for (int i = 0; i < m_Candidates.Count; i++)
			{
				CoverCandidate candidate = m_Candidates[i];
				Vector3 pos = candidate.Position + Vector3.up * 0.05f;
				if (candidate.CandidateId == m_CurrentId)
				{
					Gizmos.color = Color.white;
					Gizmos.DrawSphere(pos, 0.18f);
				}

				if (candidate.CandidateId == m_SelectedId)
				{
					Gizmos.color = Color.yellow;
					Gizmos.DrawWireSphere(pos, 0.32f);
				}
			}
		}

		Gizmos.color = m_RejectedColor;
		for (int i = 0; i < m_Rejected.Count; i++)
		{
			CoverRejectedSample rejected = m_Rejected[i];
			Vector3 pos = rejected.Position + Vector3.up * 0.05f;
			Gizmos.DrawWireSphere(pos, 0.08f);
			Gizmos.DrawLine(pos, pos + rejected.Normal.normalized * 0.4f);
		}
	}

	private void DrawRuntime()
	{
		Vector3 c = m_RegionBounds.center;
		Vector3 e = m_RegionBounds.extents;
		Vector3 a = new Vector3(c.x - e.x, 0.05f, c.z - e.z);
		Vector3 b = new Vector3(c.x + e.x, 0.05f, c.z - e.z);
		Vector3 d = new Vector3(c.x + e.x, 0.05f, c.z + e.z);
		Vector3 f = new Vector3(c.x - e.x, 0.05f, c.z + e.z);
		Debug.DrawLine(a, b, m_RegionColor, 0f, false);
		Debug.DrawLine(b, d, m_RegionColor, 0f, false);
		Debug.DrawLine(d, f, m_RegionColor, 0f, false);
		Debug.DrawLine(f, a, m_RegionColor, 0f, false);

		for (int i = 0; i < m_Candidates.Count; i++)
		{
			CoverCandidate candidate = m_Candidates[i];
			Vector3 pos = candidate.Position + Vector3.up * 0.05f;
			Color color = ColorForType(candidate.CoverType);
			Debug.DrawLine(pos, pos + Vector3.up * 0.2f, color, 0f, false);
			Debug.DrawRay(pos, candidate.Normal.normalized * 0.85f, color, 0f, false);
			if (m_TacticalActive && candidate.CandidateId == m_CurrentId)
				Debug.DrawLine(pos, pos + Vector3.up * 0.55f, Color.white, 0f, false);
			if (m_TacticalActive && candidate.CandidateId == m_SelectedId)
				Debug.DrawLine(pos, pos + Vector3.up * 0.9f, Color.yellow, 0f, false);
			if (m_PeekActive &&
			    m_PeekDecision.Kind == CoverPeekDecisionKind.Lean &&
			    candidate.CandidateId == m_PeekDecision.CandidateId)
			{
				Vector3 right = CoverPeekGeometry.RightTangent(candidate.Normal);
				float sign = m_PeekDecision.Direction == CoverPeekDirection.Left ? -1f : 1f;
				Debug.DrawRay(pos + Vector3.up * 0.3f, right * (sign * 0.7f), Color.cyan, 0f, false);
			}
		}

		for (int i = 0; i < m_Rejected.Count; i++)
		{
			CoverRejectedSample rejected = m_Rejected[i];
			Vector3 pos = rejected.Position + Vector3.up * 0.05f;
			Debug.DrawRay(pos, rejected.Normal.normalized * 0.4f, m_RejectedColor, 0f, false);
		}
	}

	private void OnGUI()
	{
		if (!m_DrawInPlay || !Application.isPlaying || !m_HasCapture)
			return;
		Camera camera = Camera.main;
		if (camera == null)
			camera = Camera.current;
		if (camera == null)
			return;

		for (int i = 0; i < m_Candidates.Count; i++)
		{
			CoverCandidate candidate = m_Candidates[i];
			Vector3 world = candidate.Position + Vector3.up * 1.35f;
			Vector3 screen = camera.WorldToScreenPoint(world);
			if (screen.z <= 0f)
				continue;
			string label = CoverClassifier.FormatTypeLabel(candidate.CandidateId, candidate.CoverType);
			if (TryGetScore(candidate.CandidateId, out float score))
			{
				label = "C" + candidate.CandidateId + "  " + score.ToString("0.0");
				if (candidate.CandidateId == m_SelectedId)
					label += m_EmergencyResult == EmergencyCoverResult.Stay ? " stay" : " *";
				else if (candidate.CandidateId == m_CurrentId && m_TacticalActive)
					label += " curr";
				else if (IsEmergencyRejected(candidate.CandidateId))
					label += " rej";
			}

			if (candidate.CandidateId == m_SelectedId)
			{
				label += "  " + CoverClassifier.FormatProtection(
					candidate.StandingProfile.Torso,
					candidate.CrouchProfile.Torso);
				label += "  " + CoverClassifier.FormatCapabilities(candidate.Capabilities);
			}

			if (TryGetOccupancyLabel(candidate.CandidateId, out string occupancy))
				label += "  " + occupancy;

			var rect = new Rect(screen.x - 48f, Screen.height - screen.y - 10f, 220f, 20f);
			GUI.color = candidate.CandidateId == m_SelectedId ? Color.white : ColorForType(candidate.CoverType);
			GUI.Label(rect, label);
		}

		if (m_TacticalActive)
		{
			GUI.color = Color.white;
			GUI.Label(
				new Rect(12f, 12f, 460f, 72f),
				"CURRENT C" + m_CurrentId + "  " + m_TacticalCurrentScore.ToString("0.0") +
				"\nBEST C" + m_SelectedId + "  " + m_TacticalBestScore.ToString("0.0") +
				"\nSWITCH COST " + m_TacticalSwitchCost.ToString("0.0") +
				"\nRESULT " + m_TacticalKind);
		}
		else if (m_EmergencyActive)
		{
			GUI.color = Color.white;
			GUI.Label(
				new Rect(12f, 12f, 420f, 22f),
				"Emergency Cover Active  " + m_EmergencyResult);
		}

		if (m_PeekActive)
		{
			GUI.color = Color.white;
			GUI.Label(new Rect(12f, 88f, 420f, 140f), FormatPeekOverlay());
		}

		GUI.color = new Color(1f, 1f, 1f, 0.9f);
		GUI.Label(
			new Rect(12f, Screen.height - 70f, 520f, 64f),
			"■ region  ● candidate  ★ current  ◆ selected  R reserved  O occupied  → lean");

		GUI.color = Color.white;
	}

	private bool TryGetScore(int _candidateId, out float _score)
	{
		_score = 0f;
		for (int i = 0; i < m_Evaluations.Count; i++)
		{
			CoverCandidate candidate = m_Evaluations[i].Candidate;
			if (candidate == null || candidate.CandidateId != _candidateId)
				continue;
			_score = m_Evaluations[i].Score;
			return true;
		}

		return false;
	}

	private string FormatPeekOverlay()
	{
		CoverPeekDebugSnapshot snap = m_PeekDecision.Snapshot;
		string noLean = snap.VisibleWithoutLean ? "visible" : "hidden";
		string selected = m_PeekDecision.Kind == CoverPeekDecisionKind.Lean
			? m_PeekDecision.Direction + " / " + m_PeekDecision.Depth
			: m_PeekDecision.Kind == CoverPeekDecisionKind.Return
				? "Return " + m_PeekDecision.Reason
				: "No Lean";
		return "C" + snap.CandidateId + "  " + snap.CoverType +
		       "\nNo Lean: Target = " + noLean +
		       "\nLeft Small:  " + FormatDepth(in snap.LeftSmall, snap.LeftAvailable) +
		       "\nLeft Medium: " + FormatDepth(in snap.LeftMedium, snap.LeftAvailable) +
		       "\nLeft Deep:   " + FormatDepth(in snap.LeftDeep, snap.LeftAvailable) +
		       "\nRight Small: " + FormatDepth(in snap.RightSmall, snap.RightAvailable) +
		       "\nSelected: " + selected;
	}

	private static string FormatDepth(in CoverPeekDepthSample _sample, bool _available)
	{
		if (!_available)
			return "n/a";
		string risk = _sample.Risk <= 0.25f ? "low" : _sample.Risk <= 0.4f ? "medium" : "high";
		return "visible = " + (_sample.Visible ? "yes" : "no") + "  risk = " + risk;
	}

	private bool IsEmergencyRejected(int _candidateId)
	{
		if (!m_EmergencyActive)
			return false;
		for (int i = 0; i < m_Evaluations.Count; i++)
		{
			CoverCandidate candidate = m_Evaluations[i].Candidate;
			if (candidate == null || candidate.CandidateId != _candidateId)
				continue;
			return !m_Evaluations[i].Valid;
		}

		return false;
	}

	private static string FormatOccupancy(in OccupancySample _sample)
	{
		if (_sample.State == CoverOccupancy.Available)
			return "AVAILABLE";
		string owner = _sample.OwnerUnitId != 0 ? " " + _sample.OwnerUnitId : string.Empty;
		if (_sample.State == CoverOccupancy.Reserved)
			return "RESERVED" + owner + " TTL " + _sample.TtlRemaining.ToString("0.0") + "s";
		return "OCCUPIED" + owner;
	}

	private Color ColorForType(CoverType _type)
	{
		switch (_type)
		{
			case CoverType.Edge:
			case CoverType.Opening:
			case CoverType.Window:
				return CoverTypeVisual.Color(_type);
			case CoverType.Crouch:
				return m_CrouchColor;
			case CoverType.Standing:
				return m_StandingColor;
			case CoverType.Partial:
				return m_PartialColor;
			case CoverType.Corner:
				return m_CornerColor;
			default:
				return m_FromCache ? m_CachedColor : m_NoneColor;
		}
	}
	#endregion
}
