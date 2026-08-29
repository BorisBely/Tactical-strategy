using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cached Search 2.0 plan. Built on Search Enter, discarded on Exit. Not rebuilt each tick.
/// </summary>
public sealed class UnitAISearchSession
{
	#region Private Fields
	private readonly List<UnitAISearchCandidate> m_Candidates = new List<UnitAISearchCandidate>(8);
	private UnitAISearchArea m_Area;
	private int m_Index;
	private UnitAISearchCompletionReason m_Completion;
	#endregion

	#region Public Properties
	public UnitAISearchArea Area => m_Area;
	public IReadOnlyList<UnitAISearchCandidate> Candidates => m_Candidates;
	public int Index => m_Index;
	public int Remaining => Mathf.Max(0, m_Candidates.Count - m_Index - 1);
	public UnitAISearchCompletionReason Completion => m_Completion;
	public bool HasCandidates => m_Candidates.Count > 0;

	public Vector3 CurrentPosition =>
		m_Index >= 0 && m_Index < m_Candidates.Count ? m_Candidates[m_Index].Position : m_Area.Center;

	public float CurrentScore =>
		m_Index >= 0 && m_Index < m_Candidates.Count ? m_Candidates[m_Index].Score : 0f;
	#endregion

	#region Public Methods
	public void Reset(in UnitAISearchArea _area)
	{
		m_Area = _area;
		m_Candidates.Clear();
		m_Index = 0;
		m_Completion = UnitAISearchCompletionReason.None;
	}

	public List<UnitAISearchCandidate> CandidateBuffer => m_Candidates;

	public void BindBuiltPlan()
	{
		m_Index = 0;
		if (m_Candidates.Count == 0)
			m_Candidates.Add(new UnitAISearchCandidate(m_Area.Center, 0f));
	}

	public bool TryAdvance()
	{
		if (m_Index + 1 >= m_Candidates.Count)
			return false;
		m_Index++;
		return true;
	}

	public void SetCompletion(UnitAISearchCompletionReason _reason)
	{
		if (m_Completion == UnitAISearchCompletionReason.None)
			m_Completion = _reason;
	}
	#endregion
}
