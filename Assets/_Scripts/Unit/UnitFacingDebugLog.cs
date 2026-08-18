using UnityEngine;

/// <summary>
/// Console snapshot of combat facing: root yaw, spine yaw, barrel-centric body, weapon model correction.
/// </summary>
public static class UnitFacingDebugLog
{
	public static bool ShouldLog(RtsUnitMember _member) =>
		_member == null || _member.IsSelected;

	public static string DiagnoseEngageGate(
		bool _isRun,
		bool _isSprint,
		bool _reloadBlocked,
		UnitWeaponReadyHandsLayer _readyHands,
		TargetSelector _targetSelector)
	{
		if (_targetSelector == null)
			return "noSelector";
		if (_targetSelector.SelectedTarget == null)
			return "noTarget";
		if (_isSprint)
			return "sprint";
		if (_isRun)
			return "run";
		if (_reloadBlocked)
			return "reload";
		if (_readyHands == null)
			return "noReadyHands";
		if (!_readyHands.IsWeaponEquipped())
			return "noWeapon";
		if (!_readyHands.WantsCombatTargetFacing())
			return $"pose={_readyHands.EffectivePoseState}(notRaised)";
		return "ok";
	}

	public static void EmitSnapshot(
		MonoBehaviour _host,
		string _rootMode,
		string _rootDetail,
		float _rootDeltaDeg,
		bool _turnSuppressed,
		UnitWeaponReadyHandsLayer _readyHands,
		UnitSpineHorizontalAim _spine,
		UnitEquipment _equipment,
		UnitWeaponAiming _weaponAiming,
		TargetSelector _targetSelector)
	{
		if (_host == null)
			return;

		Transform body = _host.transform;
		float bodyYaw = body.eulerAngles.y;
		float bodyToTarget = 0f;
		bool hasTargetBearing = TryGetBodyToTargetYaw(body, _targetSelector, out bodyToTarget);
		string targetName = _targetSelector != null && _targetSelector.SelectedTarget != null
			? _targetSelector.SelectedTarget.name
			: "none";

		float bodyBarrel = 0f;
		bool hasBarrel = UnitHorizontalFacingUtility.TryGetBodyBarrelYawOffset(body, _equipment, out bodyBarrel);
		bool barrelCentric = UnitHorizontalFacingUtility.ShouldUseBarrelCentricFacing(_readyHands);

		WeaponPoseState pose = _readyHands != null
			? _readyHands.EffectivePoseState
			: WeaponPoseState.NotReady;
		bool wantsFacing = _readyHands != null && _readyHands.WantsCombatTargetFacing();
		bool fireReady = _readyHands != null && _readyHands.IsWeaponEquippedAndReady();

		string spinePart = _spine != null ? _spine.FormatFacingDebugLine() : "spine=none";

		string weaponPart = _weaponAiming != null
			? _weaponAiming.FormatFacingDebugLine()
			: "weapon=none";

		string targetPart = hasTargetBearing
			? $"target={targetName} body↔target={bodyToTarget:F1}°"
			: $"target={targetName}";

		Debug.Log(
			$"[Facing] unit={_host.name} pose={pose} wantsFacing={(wantsFacing ? 1 : 0)} fireReady={(fireReady ? 1 : 0)} " +
			$"turnSuppress={(_turnSuppressed ? 1 : 0)}\n" +
			$"  root mode={_rootMode} Δ={_rootDeltaDeg:F2}° bodyYaw={bodyYaw:F1}° {_rootDetail}\n" +
			$"  {spinePart}\n" +
			$"  barrelCentric={(barrelCentric ? 1 : 0)} body↔barrel={(hasBarrel ? bodyBarrel.ToString("F1") : "n/a")}° {targetPart}\n" +
			$"  {weaponPart}",
			_host);
	}

	public static void EmitEvent(MonoBehaviour _host, string _event, string _detail)
	{
		if (_host == null)
			return;
		Debug.Log($"[Facing] unit={_host.name} {_event} {_detail}", _host);
	}

	private static bool TryGetBodyToTargetYaw(
		Transform _body,
		TargetSelector _targetSelector,
		out float _yaw)
	{
		_yaw = 0f;
		if (_body == null || _targetSelector == null || _targetSelector.SelectedTarget == null)
			return false;

		Vector3 toTarget = _targetSelector.GetEngageableAimPointWorld() - _body.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 1e-6f)
			return false;

		Vector3 bodyFwd = _body.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
			return false;

		_yaw = Vector3.SignedAngle(bodyFwd.normalized, toTarget.normalized, Vector3.up);
		return true;
	}
}
