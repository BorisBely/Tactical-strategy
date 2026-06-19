using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Оставшийся «лечебный ресурс» конкретной аптечки в инвентаре.
/// </summary>
[Serializable]
public sealed class MedkitRuntimeState
{
	#region Serialized Fields
	[SerializeField] private MedkitDefinition m_Definition;
	[SerializeField, Min(0)] private int m_CurrentResourcePoints;
	#endregion

	#region Public Properties
	public MedkitDefinition Definition => m_Definition;
	public int CurrentResourcePoints => m_CurrentResourcePoints;
	public int MaxResourcePoints => m_Definition != null ? m_Definition.MaxResourcePoints : 0;
	public bool HasResource => m_CurrentResourcePoints > 0;
	public bool IsDepleted => m_CurrentResourcePoints <= 0;
	public float FillRatio => MaxResourcePoints > 0
		? Mathf.Clamp01((float)m_CurrentResourcePoints / MaxResourcePoints)
		: 0f;
	#endregion

	#region Public Methods
	public void Configure(MedkitDefinition _definition, int? _startingResourcePoints = null)
	{
		m_Definition = _definition;
		int maxPoints = MaxResourcePoints;
		m_CurrentResourcePoints = _startingResourcePoints.HasValue
			? Mathf.Clamp(_startingResourcePoints.Value, 0, maxPoints > 0 ? maxPoints : _startingResourcePoints.Value)
			: maxPoints;
	}

	public bool CanAfford(int _resourceCost)
	{
		return _resourceCost > 0 && m_CurrentResourcePoints >= _resourceCost;
	}

	public bool CanTreatInjury(in InjuryUiEntry _injury)
	{
		if (m_Definition == null || !HasResource)
			return false;

		return CanAfford(m_Definition.GetInjuryTreatCost(_injury));
	}

	public bool TryConsume(int _resourceCost)
	{
		if (_resourceCost <= 0 || !CanAfford(_resourceCost))
			return false;

		m_CurrentResourcePoints = Mathf.Max(0, m_CurrentResourcePoints - _resourceCost);
		return true;
	}

	/// <summary>
	/// Списать ресурс за стабилизацию одной травмы. Лечение пока не применяется.
	/// </summary>
	public bool TryConsumeForInjury(in InjuryUiEntry _injury, out int _consumedPoints)
	{
		_consumedPoints = 0;
		if (m_Definition == null)
			return false;

		int cost = m_Definition.GetInjuryTreatCost(_injury);
		if (!TryConsume(cost))
			return false;

		_consumedPoints = cost;
		return true;
	}
	#endregion
}

/// <summary>
/// Расчёт стоимости стабилизации травм аптечкой (без применения лечения).
/// </summary>
public static class MedkitUtility
{
	#region Public Methods
	public static int CalculateTreatCost(MedkitDefinition _definition, in InjuryUiEntry _injury)
	{
		if (_definition == null)
			return 0;

		return _definition.GetInjuryTreatCost(_injury);
	}

	public static int CalculateTreatCost(MedkitDefinition _definition, IReadOnlyList<InjuryUiEntry> _injuries)
	{
		if (_definition == null || _injuries == null || _injuries.Count == 0)
			return 0;

		int total = 0;
		for (int i = 0; i < _injuries.Count; i++)
			total += _definition.GetInjuryTreatCost(_injuries[i]);

		return total;
	}

	public static bool CanTreatAll(
		MedkitRuntimeState _medkitState,
		IReadOnlyList<InjuryUiEntry> _injuries,
		out int _requiredPoints)
	{
		_requiredPoints = CalculateTreatCost(_medkitState?.Definition, _injuries);
		if (_medkitState == null || _medkitState.Definition == null)
			return false;

		return _medkitState.CanAfford(_requiredPoints);
	}

	/// <summary>
	/// Списать ресурс за набор травм в порядке UI-приоритета (тяжёлые первыми).
	/// Лечение пока не применяется — только расход аптечки.
	/// </summary>
	public static bool TryConsumeForInjuries(
		MedkitRuntimeState _medkitState,
		IReadOnlyList<InjuryUiEntry> _injuries,
		out int _consumedPoints)
	{
		_consumedPoints = 0;
		if (_medkitState == null || _medkitState.Definition == null || _injuries == null || _injuries.Count == 0)
			return false;

		IReadOnlyList<InjuryUiEntry> sorted = InjuryUiEntryUtility.SortByPriority(_injuries);
		for (int i = 0; i < sorted.Count; i++)
		{
			if (!_medkitState.TryConsumeForInjury(sorted[i], out int consumed))
				return _consumedPoints > 0;

			_consumedPoints += consumed;
		}

		return _consumedPoints > 0;
	}
	#endregion
}
