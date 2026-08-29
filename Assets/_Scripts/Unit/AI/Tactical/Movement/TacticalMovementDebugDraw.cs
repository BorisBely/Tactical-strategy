using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.1 debug: selected route, rejected candidates, score. Overlay does not Move.
/// </summary>
[DisallowMultipleComponent]
public sealed class TacticalMovementDebugDraw : MonoBehaviour
{
	#region Nested
	private struct CandidateDraw
	{
		public int Id;
		public TacticalRouteKind Kind;
		public Vector3 Origin;
		public Vector3 Destination;
		public Vector3 Hop;
		public bool HasHop;
		public bool Selected;
		public bool Viable;
		public float Score;
		public float WallProximity;
		public float Exposure;
		public float WallBias;
		public TacticalRouteRejectReason Reject;
	}
	#endregion

	#region Serialized
	[SerializeField] private bool m_DrawInPlay = true;
	#endregion

	#region Private Fields
	private readonly List<TacticalRouteWaypoint> m_Hops = new List<TacticalRouteWaypoint>(8);
	private readonly List<CandidateDraw> m_Candidates = new List<CandidateDraw>(8);
	private readonly List<TacticalCoverFilterRejection> m_RejectedCovers = new List<TacticalCoverFilterRejection>(16);
	private readonly List<TacticalExposureSample> m_Exposure = new List<TacticalExposureSample>(8);
	private float m_PeakExposure;
	private float m_AverageExposure;
	private float m_TimeAbove;
	private float m_TimeExposed;
	private Vector3 m_Origin;
	private Vector3 m_Destination;
	private Vector3 m_CurrentHop;
	private TacticalRouteKind m_Kind;
	private TacticalMovementMode m_Mode;
	private int m_SelectedId;
	private float m_SelectedScore;
	private TacticalRouteSelectReason m_Reason;
	private bool m_HasCapture;
	private TacticalRouteCommitStatus m_CommitStatus;
	private TacticalReplanAction m_ReplanAction;
	private TacticalReplanReason m_ReplanReason;
	private TacticalReplanEventKind m_LastEvent;
	private TacticalUnderFireAction m_UnderFireAction;
	private TacticalUnderFireReason m_UnderFireReason;
	private float m_UnderFireHop;
	private float m_UnderFireExposure;
	private float m_UnderFireCoverAhead;
	private TacticalArrivalResult m_ArrivalResult;
	private TacticalArrivalFailureReason m_ArrivalReason;
	private float m_ArrivalDistance;
	private int m_ArrivalCandidateId;
	private int m_ArrivalGeometry;
	private int m_ArrivalCurrentGeometry;
	private int m_ArrivalReservation;
	private TacticalMovingLeanAction m_MovingLeanAction;
	private CoverPeekDirection m_MovingLeanDirection;
	private CoverLeanLevel m_MovingLeanDepth;
	private TacticalMovingLeanReason m_MovingLeanReason;
	private float m_MovingLeanGain;
	private float m_MovingLeanExposure;
	private bool m_MovingLeanOpportunity;
	private TacticalLodTier m_LodTier;
	private TacticalLodReason m_LodReason;
	private bool m_HasAcquireLive;
	private Vector3 m_MoveDestination;
	private Vector3 m_AcquirePosition;
	private float m_AcquireTolerance = TacticalArrivalMath.DefaultAcquireToleranceMeters;
	private float m_AcquireRemaining = -1f;
	private int m_AcquireCandidateId;
	private int m_AcquireReservedId;
	private bool m_AcquireOccupied;
	#endregion

	#region Public Properties
	public bool HasCapture => m_HasCapture;
	public TacticalRouteKind Kind => m_Kind;
	public int IntermediateCount => m_Hops.Count;
	public int SelectedCandidateId => m_SelectedId;
	public float SelectedScore => m_SelectedScore;
	public TacticalReplanAction ReplanAction => m_ReplanAction;
	public TacticalReplanReason ReplanReason => m_ReplanReason;
	public TacticalUnderFireAction UnderFireAction => m_UnderFireAction;
	public TacticalUnderFireReason UnderFireReason => m_UnderFireReason;
	public TacticalArrivalResult ArrivalResult => m_ArrivalResult;
	public TacticalArrivalFailureReason ArrivalReason => m_ArrivalReason;
	public float ArrivalDistance => m_ArrivalDistance;
	public TacticalMovingLeanAction MovingLeanAction => m_MovingLeanAction;
	public CoverPeekDirection MovingLeanDirection => m_MovingLeanDirection;
	public TacticalMovingLeanReason MovingLeanReason => m_MovingLeanReason;
	public TacticalLodTier LodTier => m_LodTier;
	public TacticalLodReason LodReason => m_LodReason;
	#endregion

	#region Public Methods
	public void Capture(TacticalRoute _route, Vector3 _originFallback)
	{
		m_Hops.Clear();
		m_Candidates.Clear();
		m_RejectedCovers.Clear();
		m_Exposure.Clear();
		if (_route == null || !_route.HasDestination)
		{
			m_HasCapture = false;
			m_Kind = TacticalRouteKind.None;
			return;
		}

		m_Origin = _route.Origin;
		if (m_Origin.sqrMagnitude < 0.0001f)
			m_Origin = _originFallback;
		m_Destination = _route.Destination;
		m_CurrentHop = _route.CurrentHop;
		m_Kind = _route.Kind;
		m_Mode = _route.Mode;
		TacticalRouteMath.CopyWaypoints(_route.Intermediates, m_Hops);
		m_HasCapture = true;
	}

	public void Capture(in TacticalMovementDecision _decision, Vector3 _originFallback)
	{
		Capture(_decision.Route, _originFallback);
		m_SelectedId = _decision.SelectedCandidateId;
		m_SelectedScore = _decision.SelectedScore;
		m_Reason = _decision.SelectReason;
		m_Mode = _decision.Mode;
		m_CommitStatus = _decision.CommitStatus;
		m_ReplanAction = _decision.ReplanAction;
		m_ReplanReason = _decision.ReplanReason;
		m_LastEvent = _decision.LastEventKind;
		m_UnderFireAction = _decision.UnderFireAction;
		m_UnderFireReason = _decision.UnderFireReason;
		m_UnderFireHop = _decision.HasRoute
			? Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(
				_originFallback.sqrMagnitude > 0.0001f ? _originFallback : _decision.Origin,
				_decision.CurrentHop))
			: 0f;
		m_UnderFireExposure = _decision.SelectedScore;
		m_UnderFireCoverAhead = m_UnderFireHop;
		m_ArrivalResult = _decision.ArrivalResult;
		m_ArrivalReason = _decision.ArrivalReason;
		m_ArrivalDistance = _decision.ArrivalDistanceMeters;
		m_ArrivalCandidateId = _decision.CurrentTacticalPosition.Valid
			? _decision.CurrentTacticalPosition.CandidateId
			: _decision.ReservedCoverCandidateId;
		m_ArrivalGeometry = _decision.CurrentTacticalPosition.GeometryVersion;
		m_ArrivalCurrentGeometry = _decision.CurrentTacticalPosition.GeometryVersion;
		m_ArrivalReservation = _decision.ReservedCoverCandidateId;
		m_MovingLeanAction = _decision.MovingLeanAction;
		m_MovingLeanDirection = _decision.MovingLeanDirection;
		m_MovingLeanDepth = _decision.MovingLeanDepth;
		m_MovingLeanReason = _decision.MovingLeanReason;
		m_LodTier = _decision.LodTier;
		m_LodReason = _decision.LodReason;
		if (!m_HasCapture && _decision.HasRoute)
		{
			m_Origin = _decision.Origin.sqrMagnitude > 0.0001f ? _decision.Origin : _originFallback;
			m_Destination = _decision.Destination;
			m_CurrentHop = _decision.CurrentHop;
			m_Kind = _decision.Kind;
			m_Hops.Clear();
			m_HasCapture = true;
		}
	}

	public void CaptureArrival(in TacticalArrivalDecision _arrival)
	{
		m_ArrivalResult = _arrival.Result;
		m_ArrivalReason = _arrival.Reason;
		m_ArrivalDistance = _arrival.DistanceMeters;
		m_ArrivalCandidateId = _arrival.CandidateId;
		m_ArrivalGeometry = _arrival.GeometryVersion;
		m_ArrivalCurrentGeometry = _arrival.CurrentGeometryVersion;
		m_ArrivalReservation = _arrival.CandidateId;
		if (_arrival.Position.Valid)
			m_ArrivalCandidateId = _arrival.Position.CandidateId;
	}

	public void CaptureAcquireLive(
		Vector3 _moveDestination,
		Vector3 _acquirePosition,
		float _toleranceMeters,
		float _remainingDistance,
		int _candidateId,
		int _reservedId,
		bool _occupied)
	{
		m_HasAcquireLive = true;
		m_MoveDestination = _moveDestination;
		m_AcquirePosition = _acquirePosition;
		m_AcquireTolerance = TacticalArrivalMath.ResolveTolerance(_toleranceMeters);
		m_AcquireRemaining = _remainingDistance;
		m_AcquireCandidateId = _candidateId;
		m_AcquireReservedId = _reservedId;
		m_AcquireOccupied = _occupied;
		if (_candidateId != 0)
			m_ArrivalCandidateId = _candidateId;
		m_HasCapture = true;
	}

	public void CaptureMovingLean(in TacticalMovingLeanDecision _lean)
	{
		m_MovingLeanAction = _lean.Action;
		m_MovingLeanDirection = _lean.Direction;
		m_MovingLeanDepth = _lean.Depth;
		m_MovingLeanReason = _lean.Reason;
		m_MovingLeanGain = _lean.VisibilityGain;
		m_MovingLeanExposure = _lean.ExposureChange;
		m_MovingLeanOpportunity = _lean.Opportunity;
	}

	public void CaptureLod(in TacticalLodDecision _decision)
	{
		m_LodTier = _decision.Tier;
		m_LodReason = _decision.Reason;
		if (_decision.Tier != TacticalLodTier.None)
			m_HasCapture = true;
	}

	public void Capture(
		in TacticalMovementDecision _decision,
		in TacticalRouteDecision _evaluation,
		Vector3 _originFallback)
	{
		Capture(in _decision, _originFallback);
		CaptureEvaluations(in _evaluation);
	}

	public void CaptureCoverRejections(IReadOnlyList<TacticalCoverFilterRejection> _rejections)
	{
		m_RejectedCovers.Clear();
		if (_rejections == null)
			return;
		for (int i = 0; i < _rejections.Count; i++)
			m_RejectedCovers.Add(_rejections[i]);
	}

	public void CaptureEvaluations(in TacticalRouteDecision _evaluation)
	{
		m_Candidates.Clear();
		if (_evaluation.Evaluations == null)
			return;
		int selectedId = _evaluation.HasSelection && _evaluation.Selected.Candidate != null
			? _evaluation.Selected.Candidate.CandidateId
			: 0;
		for (int i = 0; i < _evaluation.Evaluations.Count; i++)
		{
			TacticalRouteEvaluation item = _evaluation.Evaluations[i];
			TacticalRouteCandidate candidate = item.Candidate;
			if (candidate == null)
				continue;
			bool hasHop = candidate.Intermediates != null && candidate.Intermediates.Count > 0;
			m_Candidates.Add(new CandidateDraw
			{
				Id = candidate.CandidateId,
				Kind = candidate.Kind,
				Origin = candidate.Origin,
				Destination = candidate.Destination,
				Hop = hasHop ? candidate.Intermediates[0].Position : candidate.Destination,
				HasHop = hasHop,
				Selected = candidate.CandidateId == selectedId && _evaluation.HasSelection,
				Viable = item.Viable,
				Score = item.Score,
				WallProximity = candidate.WallProximity01,
				Exposure = candidate.Exposure01,
				WallBias = item.Factors.WallBias,
				Reject = item.RejectReason
			});
			if (candidate.CandidateId == selectedId && _evaluation.HasSelection)
				CaptureExposure(candidate);
		}
	}

	private void CaptureExposure(TacticalRouteCandidate _candidate)
	{
		m_Exposure.Clear();
		if (_candidate == null || _candidate.ExposureSamples == null)
			return;
		m_PeakExposure = _candidate.PeakExposure01;
		m_AverageExposure = _candidate.Exposure01;
		m_TimeAbove = _candidate.TimeAboveThresholdSeconds;
		m_TimeExposed = _candidate.TimeExposedSeconds;
		for (int i = 0; i < _candidate.ExposureSamples.Count; i++)
			m_Exposure.Add(_candidate.ExposureSamples[i]);
	}
	#endregion

	#region Unity Lifecycle
	private void OnDrawGizmos()
	{
		if (!m_HasCapture || (!m_DrawInPlay && Application.isPlaying))
			return;

		for (int i = 0; i < m_Candidates.Count; i++)
		{
			CandidateDraw draw = m_Candidates[i];
			if (draw.Selected)
				continue;
			Gizmos.color = draw.Viable
				? new Color(0.45f, 0.55f, 0.7f, 0.55f)
				: new Color(0.55f, 0.2f, 0.2f, 0.4f);
			Vector3 previous = draw.Origin;
			if (draw.HasHop)
			{
				Gizmos.DrawLine(previous, draw.Hop);
				Gizmos.DrawSphere(draw.Hop, 0.12f);
				previous = draw.Hop;
			}

			Gizmos.DrawLine(previous, draw.Destination);
		}

		Gizmos.color = new Color(0.2f, 0.9f, 1f, 1f);
		Gizmos.DrawWireSphere(m_Origin, 0.25f);
		Gizmos.color = new Color(1f, 0.85f, 0.15f, 1f);
		Gizmos.DrawWireSphere(m_Destination, 0.35f);
		Vector3 selectedPrevious = m_Origin;
		Gizmos.color = new Color(0.35f, 1f, 0.45f, 1f);
		for (int i = 0; i < m_Hops.Count; i++)
		{
			Vector3 hop = m_Hops[i].Position;
			Gizmos.DrawLine(selectedPrevious, hop);
			Gizmos.DrawSphere(hop, 0.18f);
			selectedPrevious = hop;
		}

		Gizmos.DrawLine(selectedPrevious, m_Destination);
		for (int i = 1; i < m_Exposure.Count; i++)
		{
			Gizmos.color = RiskColor(m_Exposure[i].Risk);
			Gizmos.DrawLine(m_Exposure[i - 1].Position, m_Exposure[i].Position);
		}

		Gizmos.color = Color.white;
		Gizmos.DrawWireCube(m_CurrentHop, Vector3.one * 0.4f);
		if (m_HasAcquireLive)
		{
			Gizmos.color = m_AcquireOccupied
				? new Color(0.2f, 1f, 0.35f, 0.9f)
				: new Color(1f, 0.75f, 0.15f, 0.9f);
			Gizmos.DrawWireSphere(m_AcquirePosition, Mathf.Max(0.05f, m_AcquireTolerance));
			if (CoverSpatialMath.PlanarDistanceSqr(m_MoveDestination, m_AcquirePosition) > 0.0025f)
			{
				Gizmos.color = new Color(1f, 0.45f, 0.1f, 1f);
				Gizmos.DrawLine(m_MoveDestination, m_AcquirePosition);
				Gizmos.DrawWireCube(m_MoveDestination, Vector3.one * 0.22f);
			}
		}
		Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);
		for (int i = 0; i < m_RejectedCovers.Count; i++)
		{
			Vector3 pos = m_RejectedCovers[i].Position;
			Gizmos.DrawLine(pos + Vector3.left * 0.25f, pos + Vector3.right * 0.25f);
			Gizmos.DrawLine(pos + Vector3.forward * 0.25f, pos + Vector3.back * 0.25f);
		}

#if UNITY_EDITOR
		if (m_HasAcquireLive)
		{
			float dist = TacticalArrivalMath.DistanceMeters(transform.position, m_AcquirePosition);
			string rem = m_AcquireRemaining < 0f || float.IsPositiveInfinity(m_AcquireRemaining)
				? "n/a"
				: m_AcquireRemaining.ToString("0.00");
			UnityEditor.Handles.Label(
				transform.position + Vector3.up * 2.15f,
				"C" + m_AcquireCandidateId +
				"\nD=" + dist.ToString("0.00") + "m  Tol=" + m_AcquireTolerance.ToString("0.00") + "m" +
				"\nRemaining=" + rem +
				"\nReserved=" + (m_AcquireReservedId != 0 ? "C" + m_AcquireReservedId : "none") +
				"  Occupied=" + (m_AcquireOccupied ? "true" : "false"));
		}
		else if (m_LodTier != TacticalLodTier.None)
		{
			UnityEditor.Handles.Label(
				m_Origin + Vector3.up * 1.8f,
				m_LodTier.ToString().ToUpperInvariant());
		}
#endif
	}

	private void OnGUI()
	{
		if (!m_HasCapture || !Application.isPlaying)
			return;
		bool underFire = m_UnderFireAction != TacticalUnderFireAction.None;
		bool arrival = m_ArrivalResult != TacticalArrivalResult.None;
		bool movingLean = m_MovingLeanAction != TacticalMovingLeanAction.None || m_MovingLeanOpportunity;
		bool lod = m_LodTier != TacticalLodTier.None;
		float height = 188f;
		if (underFire)
			height += 60f;
		if (arrival)
			height += 88f;
		if (m_HasAcquireLive)
			height += 72f;
		if (movingLean)
			height += 96f;
		if (lod)
			height += 36f;
		height += 72f;
		GUI.Box(new Rect(12f, 12f, 360f, height), "Tactical Route");
		string urban = "";
		for (int i = 0; i < m_Candidates.Count; i++)
		{
			if (!m_Candidates[i].Selected)
				continue;
			urban =
				"\nexp=" + m_Candidates[i].Exposure.ToString("0.00") +
				" peak=" + m_PeakExposure.ToString("0.00") +
				" avg=" + m_AverageExposure.ToString("0.00") +
				" above=" + m_TimeAbove.ToString("0.0") + "s" +
				" exposed=" + m_TimeExposed.ToString("0.0") + "s" +
				" wall=" + m_Candidates[i].WallProximity.ToString("0.00");
			break;
		}

		string under = "";
		if (m_UnderFireAction != TacticalUnderFireAction.None)
		{
			under =
				"\nUNDER FIRE" +
				"\nCurrent hop: distance=" + m_UnderFireHop.ToString("0.0") +
				"m exp=" + m_UnderFireExposure.ToString("0.00") +
				"\nCover ahead: " + m_UnderFireCoverAhead.ToString("0.0") + "m" +
				"\nDecision: " + m_UnderFireAction + "  " + m_UnderFireReason;
		}

		string arrive = "";
		if (m_ArrivalResult != TacticalArrivalResult.None)
		{
			bool rejected = m_ArrivalResult != TacticalArrivalResult.Acquired &&
			                m_ArrivalResult != TacticalArrivalResult.Traversed;
			arrive =
				"\nTARGET C" + m_ArrivalCandidateId +
				"\nDISTANCE " + m_ArrivalDistance.ToString("0.00") + "m" +
				"\nRESERVATION E" + m_ArrivalReservation +
				"\nGEOMETRY v" + m_ArrivalGeometry +
				" CURRENT v" + m_ArrivalCurrentGeometry +
				"\nRESULT " + (rejected ? "REJECTED" : m_ArrivalResult.ToString().ToUpperInvariant()) +
				(m_ArrivalReason != TacticalArrivalFailureReason.None ? "\nREASON " + m_ArrivalReason : string.Empty);
		}

		if (m_HasAcquireLive)
		{
			float dist = TacticalArrivalMath.DistanceMeters(transform.position, m_AcquirePosition);
			string rem = m_AcquireRemaining < 0f || float.IsPositiveInfinity(m_AcquireRemaining)
				? "n/a"
				: m_AcquireRemaining.ToString("0.00") + "m";
			arrive +=
				"\nC" + m_AcquireCandidateId +
				" D=" + dist.ToString("0.00") + "m Tol=" + m_AcquireTolerance.ToString("0.00") + "m" +
				"\nRemaining " + rem +
				" Occupied=" + (m_AcquireOccupied ? "true" : "false");
		}

		string lean = "";
		if (m_MovingLeanAction != TacticalMovingLeanAction.None || m_MovingLeanOpportunity)
		{
			lean =
				"\nMOVING" +
				"\nLean Opportunity: " + (m_MovingLeanOpportunity ? "YES" : "NO") +
				"\nDirection: " + m_MovingLeanDirection +
				"\nDepth: " + m_MovingLeanDepth +
				"\nVisibility: " + m_MovingLeanGain.ToString("+0.00;-0.00") +
				"\nExposure: " + m_MovingLeanExposure.ToString("+0.00;-0.00") +
				"\nDecision: " + m_MovingLeanAction +
				(m_MovingLeanReason != TacticalMovingLeanReason.None ? "  " + m_MovingLeanReason : string.Empty);
		}

		string lodLine = "";
		if (m_LodTier != TacticalLodTier.None)
			lodLine = "\nLOD " + m_LodTier.ToString().ToUpperInvariant() + "  " + m_LodReason;
		TacticalUpdateScheduler scheduler = TacticalUpdateScheduler.Shared;
		string budget =
			"\nBudget " + scheduler.RouteBudgetUsed + " / " + scheduler.MaxRouteEvaluationsPerTick +
			"\nFull: " + scheduler.FullCount +
			"  Reduced: " + scheduler.ReducedCount +
			"  Background: " + scheduler.BackgroundCount;

		GUI.Label(
			new Rect(24f, 36f, 336f, height - 32f),
			m_Kind + " R" + m_SelectedId +
			" score=" + m_SelectedScore.ToString("0.0") +
			" mode=" + m_Mode +
			"\n" + m_Reason + " hops=" + m_Hops.Count + urban +
			"\nSTATUS " + m_CommitStatus +
			"\nLAST EVENT " + m_LastEvent +
			"\nREPLAN " + m_ReplanAction + "  " + m_ReplanReason +
			under +
			arrive +
			lean +
			lodLine +
			budget);
	}

	private static Color RiskColor(TacticalExposureRisk _risk)
	{
		if (_risk == TacticalExposureRisk.Critical)
			return new Color(0.95f, 0.15f, 0.1f, 1f);
		if (_risk == TacticalExposureRisk.Dangerous)
			return new Color(0.95f, 0.55f, 0.1f, 1f);
		if (_risk == TacticalExposureRisk.Exposed)
			return new Color(0.9f, 0.85f, 0.2f, 1f);
		return new Color(0.25f, 0.85f, 0.35f, 1f);
	}
	#endregion
}
