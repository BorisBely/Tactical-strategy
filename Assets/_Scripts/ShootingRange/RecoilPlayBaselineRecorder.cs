using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Captures range-face hits into A1–A5 groups. Group center = mean XY; spread = max pairwise about that center.
/// Also logs RecoilOffset at the shot that produced the hit (hitscan uses Offset before this shot's kick).
/// </summary>
[DisallowMultipleComponent]
public sealed class RecoilPlayBaselineRecorder : MonoBehaviour
{
	#region Nested Types
	public struct ShotCapture
	{
		public float OffsetXCm;
		public float OffsetYCm;
		public Vector2 RecoilOffsetDeg;
		public int RecoilShotIndex;
		public float DistanceMeters;
		public float Time;
	}

	public struct RepeatSummary
	{
		public RecoilPlayBaselineProtocol.CaseId Case;
		public int RepeatIndex;
		public int ShotCount;
		public float CenterXCm;
		public float CenterYCm;
		public float CenterAbsCm;
		public float SpreadDiameterCm;
		public Vector2 RecoilOffsetAtLastShotDeg;
		public int RecoilShotIndexAtLastShot;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private RecoilPlayBaselineProtocol.CaseId m_ActiveCase = RecoilPlayBaselineProtocol.CaseId.A1AimingStand50;
	[SerializeField] private int m_ActiveRepeatIndex = 1;
	[SerializeField] private bool m_CaptureEnabled;
	[SerializeField, Min(0.1f)] private float m_BurstGapSeconds = RecoilPlayBaselineProtocol.BurstGapSeconds;
	[TextArea(8, 24)]
	[SerializeField] private string m_LastPlaySection;
	#endregion

	#region Private Fields
	private readonly List<ShotCapture> m_CurrentShots = new List<ShotCapture>(16);
	private readonly List<RepeatSummary> m_Repeats = new List<RepeatSummary>(24);
	private float m_LastShotTime = -999f;
	#endregion

	#region Public Properties
	public RecoilPlayBaselineProtocol.CaseId ActiveCase => m_ActiveCase;
	public string LastPlaySection => m_LastPlaySection;
	public IReadOnlyList<RepeatSummary> Repeats => m_Repeats;
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		ShootingRangeHitLogger.HitRecorded += HandleHitRecorded;
	}

	private void OnDisable()
	{
		ShootingRangeHitLogger.HitRecorded -= HandleHitRecorded;
	}
	#endregion

	#region Public Methods
	[ContextMenu("Begin A1 Aiming Stand 50m")]
	public void BeginA1() => BeginCase(RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);

	[ContextMenu("Begin A2 Aiming Walk 50m")]
	public void BeginA2() => BeginCase(RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50);

	[ContextMenu("Begin A3 HipFire Stand 15m")]
	public void BeginA3() => BeginCase(RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15);

	[ContextMenu("Begin A4 Aiming Crouch 50m")]
	public void BeginA4() => BeginCase(RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50);

	[ContextMenu("Begin A5 pause 0.4s")]
	public void BeginA5() => BeginCase(RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50);

	public void BeginCase(RecoilPlayBaselineProtocol.CaseId _case)
	{
		FlushCurrentBurstIfNeeded(true);
		m_ActiveCase = _case;
		m_ActiveRepeatIndex = CountRepeats(_case) + 1;
		m_CaptureEnabled = true;
		m_CurrentShots.Clear();
		Debug.Log("[RecoilPlayBaseline] Begin " + RecoilPlayBaselineProtocol.CaseLabel(_case) +
		          " repeat " + m_ActiveRepeatIndex);
	}

	[ContextMenu("Complete Repeat")]
	public void CompleteRepeat()
	{
		FlushCurrentBurstIfNeeded(true);
		m_LastPlaySection = BuildPlaySection();
		Debug.Log("[RecoilPlayBaseline] Repeat complete.\n" + m_LastPlaySection);
	}

	[ContextMenu("Clear Captures")]
	public void ClearCaptures()
	{
		m_CurrentShots.Clear();
		m_Repeats.Clear();
		m_LastPlaySection = string.Empty;
		m_CaptureEnabled = false;
	}

	public float GetMedianAbsCm(RecoilPlayBaselineProtocol.CaseId _case, int _shotCount)
	{
		List<float> values = CollectAbsCm(_case, _shotCount);
		if (values.Count < RecoilPlayBaselineProtocol.RepeatCount)
			return -1f;
		values.Sort();
		return RecoilPlayBaselineProtocol.Median3(values[0], values[1], values[2]);
	}
	#endregion

	#region Private Methods
	private void HandleHitRecorded(ShootingRangeHitRecord _record)
	{
		if (!m_CaptureEnabled || !isActiveAndEnabled)
			return;
		if (!_record.Accuracy.IsValid)
			return;

		if (m_CurrentShots.Count > 0 &&
		    Time.time - m_LastShotTime > m_BurstGapSeconds &&
		    m_ActiveCase != RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50)
			FlushCurrentBurstIfNeeded(true);

		m_LastShotTime = Time.time;
		m_CurrentShots.Add(new ShotCapture
		{
			OffsetXCm = _record.OffsetXCm,
			OffsetYCm = _record.OffsetYCm,
			RecoilOffsetDeg = _record.RecoilOffsetDegrees,
			RecoilShotIndex = _record.RecoilShotIndex,
			DistanceMeters = _record.ShotDistanceMeters,
			Time = Time.time
		});

		int needed = m_ActiveCase == RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50
			? 4
			: 8;
		if (m_CurrentShots.Count >= needed)
			FlushCurrentBurstIfNeeded(true);
	}

	private void FlushCurrentBurstIfNeeded(bool _force)
	{
		if (m_CurrentShots.Count == 0)
			return;
		if (!_force && m_CurrentShots.Count < 1)
			return;

		if (m_ActiveCase == RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50)
			StorePrefix(4);
		else
		{
			StorePrefix(1);
			StorePrefix(3);
			StorePrefix(5);
			StorePrefix(8);
		}

		m_CurrentShots.Clear();
		m_ActiveRepeatIndex = CountRepeats(m_ActiveCase) + 1;
	}

	private void StorePrefix(int _shotCount)
	{
		if (m_CurrentShots.Count < _shotCount)
			return;

		float sumX = 0f;
		float sumY = 0f;
		for (int i = 0; i < _shotCount; i++)
		{
			sumX += m_CurrentShots[i].OffsetXCm;
			sumY += m_CurrentShots[i].OffsetYCm;
		}

		float meanX = sumX / _shotCount;
		float meanY = sumY / _shotCount;
		float spread = 0f;
		for (int i = 0; i < _shotCount; i++)
		{
			for (int j = i + 1; j < _shotCount; j++)
			{
				float dx = m_CurrentShots[i].OffsetXCm - m_CurrentShots[j].OffsetXCm;
				float dy = m_CurrentShots[i].OffsetYCm - m_CurrentShots[j].OffsetYCm;
				spread = Mathf.Max(spread, Mathf.Sqrt(dx * dx + dy * dy));
			}
		}

		ShotCapture last = m_CurrentShots[_shotCount - 1];
		m_Repeats.Add(new RepeatSummary
		{
			Case = m_ActiveCase,
			RepeatIndex = m_ActiveRepeatIndex,
			ShotCount = _shotCount,
			CenterXCm = meanX,
			CenterYCm = meanY,
			CenterAbsCm = Mathf.Sqrt(meanX * meanX + meanY * meanY),
			SpreadDiameterCm = spread,
			RecoilOffsetAtLastShotDeg = last.RecoilOffsetDeg,
			RecoilShotIndexAtLastShot = last.RecoilShotIndex
		});
	}

	private int CountRepeats(RecoilPlayBaselineProtocol.CaseId _case)
	{
		int maxRepeat = 0;
		for (int i = 0; i < m_Repeats.Count; i++)
		{
			if (m_Repeats[i].Case == _case)
				maxRepeat = Mathf.Max(maxRepeat, m_Repeats[i].RepeatIndex);
		}

		return maxRepeat;
	}

	private List<float> CollectAbsCm(RecoilPlayBaselineProtocol.CaseId _case, int _shotCount)
	{
		var values = new List<float>(4);
		for (int i = 0; i < m_Repeats.Count; i++)
		{
			if (m_Repeats[i].Case == _case && m_Repeats[i].ShotCount == _shotCount)
				values.Add(m_Repeats[i].CenterAbsCm);
		}

		return values;
	}

	private string BuildPlaySection()
	{
		var lines = new List<string>(32);
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 1, "A1 shot1 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 3, "A1 shot3 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 8, "A1 shot8 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50, 5, "A2 shot5 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15, 5, "A3 shot5 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50, 5, "A4 shot5 |Offset|_cm");
		AppendMedianLine(lines, RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50, 4, "A5 shot4 |Offset|_cm");
		AppendIndexNote(lines);
		return string.Join("\n", lines);
	}

	private void AppendMedianLine(
		List<string> _lines,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount,
		string _label)
	{
		float median = GetMedianAbsCm(_case, _shotCount);
		if (median < 0f)
			_lines.Add(_label + ": PLAY_PENDING (need 3 repeats)");
		else
			_lines.Add(_label + ": median " + median.ToString("F1") + " cm (n=3)");
	}

	private void AppendIndexNote(List<string> _lines)
	{
		for (int i = 0; i < m_Repeats.Count; i++)
		{
			if (m_Repeats[i].Case != RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50 ||
			    m_Repeats[i].ShotCount != 4)
				continue;
			_lines.Add(
				"A5 RecoilShotIndex at shot4=" + m_Repeats[i].RecoilShotIndexAtLastShot +
				" RecoilOffset=" + m_Repeats[i].RecoilOffsetAtLastShotDeg.magnitude.ToString("F3") +
				"° (must not reset to 0/1 if StopFiring snapped state)");
		}
	}
	#endregion
}
