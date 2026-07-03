#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Editor/dev logging for formation slot assignment: rank leader, min-cost matching, travel distances.
/// </summary>
public static class FormationAssignmentDebug
{
	#region Public Types
	public readonly struct AssignmentStep
	{
		public AssignmentStep(int _stepIndex, int _unitIndex, int _slotIndex, float _distanceMeters, string _reason)
		{
			StepIndex = _stepIndex;
			UnitIndex = _unitIndex;
			SlotIndex = _slotIndex;
			DistanceMeters = _distanceMeters;
			Reason = _reason;
		}

		public int StepIndex { get; }
		public int UnitIndex { get; }
		public int SlotIndex { get; }
		public float DistanceMeters { get; }
		public string Reason { get; }
	}
	#endregion

	#region Public Fields
	public static bool LoggingEnabled = true;

	/// <summary>Warn when a unit must travel farther than this to reach its assigned slot (meters).</summary>
	public static float LongJumpDistanceMeters = 8f;
	#endregion

	#region Public Methods
	public static void LogAssignment(
		string _context,
		FormationType _formation,
		Vector3 _centerPoint,
		Vector3 _forward,
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationLayoutUtility.FormationSlotLayout> _slots,
		Vector3[] _slotWorldPositions,
		int _leaderUnitIndex,
		int[] _unitToSlotIndex,
		FormationType _sortMode,
		IReadOnlyList<AssignmentStep> _steps)
	{
		if (!LoggingEnabled || _units == null || _slots == null || _unitToSlotIndex == null)
			return;

		var sb = new StringBuilder(2048);
		sb.Append("[FormationAssign] ").Append(_context)
			.Append(" | ").Append(_formation)
			.Append(" | units=").Append(_units.Count)
			.Append(" | slots=").Append(_slots.Count)
			.AppendLine();
		sb.Append("  center=").Append(FormatVector(_centerPoint))
			.Append(" | forward=").Append(FormatVector(_forward))
			.Append(" | sortMode=").Append(_sortMode);

		LogSlotTargets(sb, _slots, _slotWorldPositions);
		LogRankOverview(sb, _units, _leaderUnitIndex);
		LogGreedySteps(sb, _units, _steps);
		LogPerUnitDistanceMatrix(sb, _units, _slots, _slotWorldPositions, _unitToSlotIndex, _leaderUnitIndex);
		LogSummary(sb, _units, _slotWorldPositions, _unitToSlotIndex);

		Debug.Log(sb.ToString());
	}

	public static void SetLoggingEnabled(bool _enabled)
	{
		LoggingEnabled = _enabled;
	}
	#endregion

	#region Private Methods
	private static void LogSlotTargets(
		StringBuilder _sb,
		IReadOnlyList<FormationLayoutUtility.FormationSlotLayout> _slots,
		Vector3[] _slotWorldPositions)
	{
		_sb.AppendLine();
		_sb.AppendLine("  === Slot targets (world positions) ===");
		if (_slotWorldPositions == null || _slots == null)
			return;

		for (int slotIndex = 0; slotIndex < _slots.Count && slotIndex < _slotWorldPositions.Length; slotIndex++)
		{
			FormationLayoutUtility.FormationSlotLayout slot = _slots[slotIndex];
			_sb.Append("    slot ").Append(slotIndex)
				.Append(" | world=").Append(FormatVector(_slotWorldPositions[slotIndex]))
				.Append(" | local=(").Append(slot.LocalOffset.x.ToString("F2"))
				.Append(", ").Append(slot.LocalOffset.z.ToString("F2")).Append(')')
				.AppendLine();
		}
	}

	private static void LogRankOverview(StringBuilder _sb, IReadOnlyList<RtsUnitMember> _units, int _leaderUnitIndex)
	{
		_sb.AppendLine();
		_sb.AppendLine("  === Units before assignment ===");
		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
			{
				_sb.Append("    [").Append(i).AppendLine("] <null>");
				continue;
			}

			int rankIndex = ResolveUnitRankIndex(unit);
			string rankLabel = ResolveUnitRankLabel(unit);
			_sb.Append("    [").Append(i).Append("] ").Append(unit.name)
				.Append(" | rank=").Append(rankIndex).Append(" (").Append(rankLabel).Append(")")
				.Append(" | pos=").Append(FormatVector(unit.transform.position));
			if (i == _leaderUnitIndex)
				_sb.Append("  <-- rank leader");
			_sb.AppendLine();
		}
	}

	private static void LogGreedySteps(
		StringBuilder _sb,
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<AssignmentStep> _steps)
	{
		_sb.AppendLine();
		_sb.AppendLine("  === Assignment steps ===");
		if (_steps == null || _steps.Count == 0)
		{
			_sb.AppendLine("    (none)");
			return;
		}

		for (int i = 0; i < _steps.Count; i++)
		{
			AssignmentStep step = _steps[i];
			string unitName = step.UnitIndex >= 0 && step.UnitIndex < _units.Count && _units[step.UnitIndex] != null
				? _units[step.UnitIndex].name
				: "?";
			_sb.Append("    step ").Append(step.StepIndex)
				.Append(": unit[").Append(step.UnitIndex).Append("] ").Append(unitName)
				.Append(" -> slot ").Append(step.SlotIndex)
				.Append(" | dist=").Append(step.DistanceMeters.ToString("F2")).Append('m')
				.Append(" | ").Append(step.Reason)
				.AppendLine();
		}
	}

	private static void LogPerUnitDistanceMatrix(
		StringBuilder _sb,
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationLayoutUtility.FormationSlotLayout> _slots,
		Vector3[] _slotWorldPositions,
		int[] _unitToSlotIndex,
		int _leaderUnitIndex)
	{
		_sb.AppendLine();
		_sb.AppendLine("  === Per-unit distances to every slot ===");
		if (_slotWorldPositions == null || _slotWorldPositions.Length == 0)
			return;

		for (int unitIndex = 0; unitIndex < _units.Count; unitIndex++)
		{
			RtsUnitMember unit = _units[unitIndex];
			if (unit == null)
				continue;

			Vector3 unitPos = unit.transform.position;
			int chosenSlot = unitIndex < _unitToSlotIndex.Length ? _unitToSlotIndex[unitIndex] : -1;
			int nearestSlot = FindNearestSlot(unitPos, _slotWorldPositions);
			float nearestDist = nearestSlot >= 0
				? Vector3.Distance(unitPos, _slotWorldPositions[nearestSlot])
				: -1f;

			_sb.AppendLine();
			_sb.Append("  --- Unit[").Append(unitIndex).Append("] ").Append(unit.name)
				.Append(" @ ").Append(FormatVector(unitPos)).Append(" ---");
			if (unitIndex == _leaderUnitIndex)
				_sb.Append(" (rank leader)");
			_sb.AppendLine();

			if (chosenSlot >= 0 && chosenSlot != nearestSlot)
			{
				_sb.Append("    note: nearest slot ").Append(nearestSlot)
					.Append(" @ ").Append(nearestDist.ToString("F2")).Append("m");
				if (unitIndex == _leaderUnitIndex && chosenSlot == 0)
					_sb.Append(", but slot 0 reserved for rank leader");
				else
					_sb.Append(", but assigned slot ").Append(chosenSlot).Append(" by min-cost matching");
				_sb.AppendLine();
			}

			for (int slotIndex = 0; slotIndex < _slotWorldPositions.Length; slotIndex++)
			{
				float distance = Vector3.Distance(unitPos, _slotWorldPositions[slotIndex]);
				FormationLayoutUtility.FormationSlotLayout slot = slotIndex < _slots.Count ? _slots[slotIndex] : default;
				_sb.Append("      slot ").Append(slotIndex)
					.Append(" | world=").Append(FormatVector(_slotWorldPositions[slotIndex]))
					.Append(" | local=(").Append(slot.LocalOffset.x.ToString("F2"))
					.Append(", ").Append(slot.LocalOffset.z.ToString("F2")).Append(")")
					.Append(" | dist=").Append(distance.ToString("F2")).Append('m');

				if (slotIndex == chosenSlot)
					_sb.Append(" | >>> CHOSEN <<<");
				if (slotIndex == nearestSlot)
					_sb.Append(" | NEAREST");
				if (distance >= LongJumpDistanceMeters)
					_sb.Append(" | long jump");

				_sb.AppendLine();
			}
		}
	}

	private static int FindNearestSlot(Vector3 _unitPosition, Vector3[] _slotWorldPositions)
	{
		int nearestSlot = -1;
		float nearestDistSqr = float.MaxValue;
		for (int slotIndex = 0; slotIndex < _slotWorldPositions.Length; slotIndex++)
		{
			float distSqr = (_unitPosition - _slotWorldPositions[slotIndex]).sqrMagnitude;
			if (distSqr < nearestDistSqr)
			{
				nearestDistSqr = distSqr;
				nearestSlot = slotIndex;
			}
		}

		return nearestSlot;
	}

	private static void LogSummary(
		StringBuilder _sb,
		IReadOnlyList<RtsUnitMember> _units,
		Vector3[] _slotWorldPositions,
		int[] _unitToSlotIndex)
	{
		float totalTravel = 0f;
		float maxTravel = 0f;
		float naiveNearestSum = 0f;
		int assigned = 0;
		int unassigned = 0;
		string maxTravelUnit = "—";
		int maxTravelSlot = -1;

		for (int unitIndex = 0; unitIndex < _units.Count; unitIndex++)
		{
			RtsUnitMember unit = _units[unitIndex];
			int slotIndex = unitIndex < _unitToSlotIndex.Length ? _unitToSlotIndex[unitIndex] : -1;
			if (unit == null || slotIndex < 0 || _slotWorldPositions == null || slotIndex >= _slotWorldPositions.Length)
			{
				unassigned++;
				continue;
			}

			Vector3 unitPos = unit.transform.position;
			float travel = Vector3.Distance(unitPos, _slotWorldPositions[slotIndex]);
			totalTravel += travel;
			assigned++;

			int nearestSlot = FindNearestSlot(unitPos, _slotWorldPositions);
			if (nearestSlot >= 0)
				naiveNearestSum += Vector3.Distance(unitPos, _slotWorldPositions[nearestSlot]);

			if (travel > maxTravel)
			{
				maxTravel = travel;
				maxTravelUnit = unit.name;
				maxTravelSlot = slotIndex;
			}
		}

		float avgTravel = assigned > 0 ? totalTravel / assigned : 0f;
		_sb.AppendLine();
		_sb.Append("  === SUMMARY === assigned=").Append(assigned)
			.Append(" | unassigned=").Append(unassigned)
			.Append(" | totalTravel=").Append(totalTravel.ToString("F2")).Append('m')
			.Append(" | avg=").Append(avgTravel.ToString("F2")).Append('m')
			.Append(" | max=").Append(maxTravel.ToString("F2")).Append("m (")
			.Append(maxTravelUnit).Append(" -> slot ").Append(maxTravelSlot).Append(')');

		if (assigned > 0)
		{
			_sb.Append(" | naiveNearestSum=").Append(naiveNearestSum.ToString("F2")).Append('m');
			_sb.Append(" | overheadVsNaiveNearest=+").Append((totalTravel - naiveNearestSum).ToString("F2")).Append('m');
		}

		if (assigned > 1 && maxTravel > avgTravel * 2f && maxTravel >= LongJumpDistanceMeters * 0.5f)
			_sb.Append(" | note: uneven shuffle (max >> avg)");
	}

	private static int ResolveUnitRankIndex(RtsUnitMember _unit)
	{
		if (_unit == null)
			return -1;

		UnitCombatStats stats = _unit.GetComponent<UnitCombatStats>();
		if (stats == null)
			return -1;

		return UnitCombatRankCycle.GetRankAssetNameIndex(stats.RankPreset);
	}

	private static string ResolveUnitRankLabel(RtsUnitMember _unit)
	{
		if (_unit == null)
			return "—";

		UnitCombatStats stats = _unit.GetComponent<UnitCombatStats>();
		if (stats == null || stats.RankPreset == null)
			return "none";

		return UnitCombatRankCycle.ResolveRankLabel(stats.RankPreset);
	}

	private static string FormatVector(Vector3 _vector)
	{
		return $"({_vector.x:F1}, {_vector.y:F1}, {_vector.z:F1})";
	}
	#endregion
}
#endif
