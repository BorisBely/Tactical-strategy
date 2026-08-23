using UnityEngine;

/// <summary>
/// Per-observer G8 scan counters. Compute cost only — not detection quality.
/// </summary>
public sealed class VisionScanStats
{
	public int VisionScanCount;
	public int CandidateCount;
	public int RangePassCount;
	public int FovPassCount;
	public int LosCheckCount;
	public int HitZoneCheckCount;
	public int ContactsCreated;
	public int ContactsUpdated;

	public int LastScanCandidateCount;
	public int LastScanRangePassCount;
	public int LastScanFovPassCount;
	public int LastScanLosCheckCount;
	public int LastScanHitZoneCheckCount;
	public int ScopeDetailedQueryCount;
	public int LastScanScopeDetailedQueryCount;
	public int SkippedDuplicateCount;
	public int LastScanSkippedDuplicateCount;
	public int CachedLosCount;
	public int LastScanCachedLosCount;
	public int ScopeSweepScanCount;
	public int ScopeLiveLosCount;
	public int LastScanScopeLiveLosCount;
	public int MaxScopeLiveLosCount;
	public int QualityEvalCount;
	public int LastScanQualityEvalCount;

	public int MaxLosCheckCount;
	public int MaxHitZoneCheckCount;
	public int MaxCandidateCount;

	public readonly int[] CandidatesByBucket = new int[4];

	public int FrameScanCount;
	public int FrameLosCheckCount;
	public float FrameElapsedMs;
	public float AverageFrameMs;
	public float MaxFrameMs;

	private int m_FrameSamples;
	private float m_FrameMsAccum;
	private int m_TrackedFrame = -1;

	public void BeginFrame(int _frameIndex)
	{
		if (m_TrackedFrame == _frameIndex)
			return;
		m_TrackedFrame = _frameIndex;
		FrameScanCount = 0;
		FrameLosCheckCount = 0;
		FrameElapsedMs = 0f;
	}

	public void BeginScan()
	{
		LastScanCandidateCount = 0;
		LastScanRangePassCount = 0;
		LastScanFovPassCount = 0;
		LastScanLosCheckCount = 0;
		LastScanHitZoneCheckCount = 0;
		LastScanScopeDetailedQueryCount = 0;
		LastScanSkippedDuplicateCount = 0;
		LastScanCachedLosCount = 0;
		LastScanScopeLiveLosCount = 0;
		LastScanQualityEvalCount = 0;
	}

	public void EndScan()
	{
		VisionScanCount++;
		FrameScanCount++;
		CandidateCount += LastScanCandidateCount;
		RangePassCount += LastScanRangePassCount;
		FovPassCount += LastScanFovPassCount;
		LosCheckCount += LastScanLosCheckCount;
		HitZoneCheckCount += LastScanHitZoneCheckCount;
		ScopeDetailedQueryCount += LastScanScopeDetailedQueryCount;
		SkippedDuplicateCount += LastScanSkippedDuplicateCount;
		CachedLosCount += LastScanCachedLosCount;
		ScopeLiveLosCount += LastScanScopeLiveLosCount;
		if (LastScanScopeLiveLosCount > MaxScopeLiveLosCount)
			MaxScopeLiveLosCount = LastScanScopeLiveLosCount;
		QualityEvalCount += LastScanQualityEvalCount;
		FrameLosCheckCount += LastScanLosCheckCount;
		if (LastScanLosCheckCount > MaxLosCheckCount)
			MaxLosCheckCount = LastScanLosCheckCount;
		if (LastScanHitZoneCheckCount > MaxHitZoneCheckCount)
			MaxHitZoneCheckCount = LastScanHitZoneCheckCount;
		if (LastScanCandidateCount > MaxCandidateCount)
			MaxCandidateCount = LastScanCandidateCount;
	}

	public void AddCandidates(int _count)
	{
		LastScanCandidateCount += Mathf.Max(0, _count);
	}

	public void AddRangePass()
	{
		LastScanRangePassCount++;
	}

	public void AddFovPass()
	{
		LastScanFovPassCount++;
	}

	public void AddLosCheck()
	{
		LastScanLosCheckCount++;
	}

	public void AddHitZoneCheck()
	{
		LastScanHitZoneCheckCount++;
	}

	public void AddScopeDetailedQuery()
	{
		LastScanScopeDetailedQueryCount++;
	}

	public void AddSkippedDuplicate()
	{
		LastScanSkippedDuplicateCount++;
	}

	public void AddCachedLos()
	{
		LastScanCachedLosCount++;
	}

	public void AddScopeLiveLos()
	{
		LastScanScopeLiveLosCount++;
	}

	public void AddScopeSweepScan()
	{
		ScopeSweepScanCount++;
	}

	public void AddQualityEval()
	{
		LastScanQualityEvalCount++;
	}

	public void AddCandidateDistance(float _distanceMeters)
	{
		int idx = (int)VisionLodMath.Bucket(_distanceMeters);
		CandidatesByBucket[idx]++;
	}

	public void NotifyContactCreated()
	{
		ContactsCreated++;
	}

	public void NotifyContactUpdated()
	{
		ContactsUpdated++;
	}

	public void AddFrameMilliseconds(float _milliseconds)
	{
		float ms = Mathf.Max(0f, _milliseconds);
		FrameElapsedMs += ms;
		m_FrameMsAccum += ms;
		m_FrameSamples++;
		if (ms > MaxFrameMs)
			MaxFrameMs = ms;
		AverageFrameMs = m_FrameSamples > 0 ? m_FrameMsAccum / m_FrameSamples : 0f;
	}

	public void Reset()
	{
		VisionScanCount = 0;
		CandidateCount = 0;
		RangePassCount = 0;
		FovPassCount = 0;
		LosCheckCount = 0;
		HitZoneCheckCount = 0;
		ContactsCreated = 0;
		ContactsUpdated = 0;
		LastScanCandidateCount = 0;
		LastScanRangePassCount = 0;
		LastScanFovPassCount = 0;
		LastScanLosCheckCount = 0;
		LastScanHitZoneCheckCount = 0;
		ScopeDetailedQueryCount = 0;
		LastScanScopeDetailedQueryCount = 0;
		SkippedDuplicateCount = 0;
		LastScanSkippedDuplicateCount = 0;
		CachedLosCount = 0;
		LastScanCachedLosCount = 0;
		ScopeSweepScanCount = 0;
		ScopeLiveLosCount = 0;
		LastScanScopeLiveLosCount = 0;
		MaxScopeLiveLosCount = 0;
		QualityEvalCount = 0;
		LastScanQualityEvalCount = 0;
		MaxLosCheckCount = 0;
		MaxHitZoneCheckCount = 0;
		MaxCandidateCount = 0;
		for (int i = 0; i < CandidatesByBucket.Length; i++)
			CandidatesByBucket[i] = 0;
		FrameScanCount = 0;
		FrameLosCheckCount = 0;
		FrameElapsedMs = 0f;
		AverageFrameMs = 0f;
		MaxFrameMs = 0f;
		m_FrameSamples = 0;
		m_FrameMsAccum = 0f;
		m_TrackedFrame = -1;
	}
}
