using UnityEngine;

/// <summary>
/// Единая проверка «сражённого» юнита для меню ПКМ и механики оттаскивания.
/// </summary>
public static class UnitFallenStateUtility
{
	public static bool IsRtsControllable(RtsUnitMember _unit)
	{
		if (_unit == null)
			return false;
		return _unit.isActiveAndEnabled && _unit.IsPlayerSelectable && !MissionPrepSquadSpawner.IsMissionPrepPresentationMember(_unit) && !UnitVehicleMountState.IsUnitMounted(_unit) && !IsFallenOrDead(_unit);
	}

	public static bool IsFallenOrDead(RtsUnitMember _unit)
	{
		return TryDescribeFallenState(_unit, out _);
	}

	public static bool TryDescribeFallenState(RtsUnitMember _unit, out string _description)
	{
		_description = "unit is null";
		if (_unit == null)
			return false;

		// RtsUnitMember намеренно disabled у не-игроков (UnitFactionConfigurator.ApplyRoleComponents).
		if (!_unit.gameObject.activeInHierarchy)
		{
			_description = "unit hierarchy inactive";
			return false;
		}

		UnitConsciousness consciousness = _unit.GetComponentInChildren<UnitConsciousness>(true);
		bool isConscious = consciousness == null || consciousness.IsConscious;

		UnitHealth health = _unit.GetComponentInChildren<UnitHealth>(true);
		bool isDead = health != null && health.IsDead;

		UnitRagdollController ragdoll = _unit.GetComponentInChildren<UnitRagdollController>(true);
		bool isRagdollActive = ragdoll != null && ragdoll.IsRagdollActive;

		_description =
			$"instanceId={_unit.GetInstanceID()}, rtsMemberEnabled={_unit.enabled}, conscious={isConscious}, dead={isDead}, ragdoll={isRagdollActive}";

		if (consciousness != null && !consciousness.IsConscious)
			return true;

		if (isDead)
			return true;

		return isRagdollActive;
	}
}
