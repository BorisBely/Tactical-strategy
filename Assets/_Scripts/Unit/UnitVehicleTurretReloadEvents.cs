using UnityEngine;

/// <summary>
/// Animation events перезарядки турели на юните-стрелке. Пробрасывает в <see cref="VehicleTurretReloadController"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitVehicleTurretReloadEvents : MonoBehaviour
{
	#region Private Fields
	private VehicleTurretReloadController m_BoundController;
	#endregion

	#region Public Properties
	public bool IsBound => m_BoundController != null;
	public bool IsReloadAnimationActive => m_BoundController != null && m_BoundController.IsReloading;
	public bool IsReloadBusy => m_BoundController != null && m_BoundController.IsReloadBusy;
	public bool UseLeftHandIk => m_BoundController != null && m_BoundController.UseLeftHandIk;
	public bool UseRightHandIk => m_BoundController != null && m_BoundController.UseRightHandIk;
	public bool UseNotReadyIkTargets => m_BoundController != null && m_BoundController.UseNotReadyIkTargets;
	public bool UseHandleNotReadyIkTargets =>
		m_BoundController != null && m_BoundController.UseHandleNotReadyIkTargets;
	public Transform RightHandHandleIkTarget =>
		m_BoundController != null ? m_BoundController.RightHandHandleIkTarget : null;
	public Transform LeftHandHandleIkTarget =>
		m_BoundController != null ? m_BoundController.LeftHandHandleIkTarget : null;
	#endregion

	#region Public Methods
	public static UnitVehicleTurretReloadEvents GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;
		if (!_unitObject.TryGetComponent(out UnitVehicleTurretReloadEvents events))
			events = _unitObject.AddComponent<UnitVehicleTurretReloadEvents>();
		VehicleGunnerReloadBodyMotion.GetOrAdd(_unitObject);
		return events;
	}

	public void Bind(VehicleTurretReloadController _controller)
	{
		m_BoundController = _controller;
	}

	public void Unbind(VehicleTurretReloadController _controller)
	{
		if (m_BoundController == _controller)
			m_BoundController = null;
	}

	public bool TryStartReload(VehicleTurretReloadController _controller)
	{
		if (_controller == null || IsReloadBusy)
			return false;
		RtsUnitMember gunner = GetComponentInParent<RtsUnitMember>();
		return gunner != null && _controller.TryStartReload(gunner);
	}

	public bool TryStartReloadFromGunner()
	{
		return TryStartReload(m_BoundController);
	}

	public void AnimationEvent_TurretAttachMagToLeftHand() => m_BoundController?.AnimationEvent_TurretAttachMagToLeftHand();
	public void AnimationEvent_TurretShowBelt() => m_BoundController?.AnimationEvent_TurretShowBelt();
	public void AnimationEvent_TurretDisableRightHandIk() => m_BoundController?.AnimationEvent_TurretDisableRightHandIk();
	public void AnimationEvent_TurretSwapEmptyForFullMag() => m_BoundController?.AnimationEvent_TurretSwapEmptyForFullMag();
	public void AnimationEvent_TurretEnableRightHandIk() => m_BoundController?.AnimationEvent_TurretEnableRightHandIk();
	public void AnimationEvent_TurretReturnMagToWeapon() => m_BoundController?.AnimationEvent_TurretReturnMagToWeapon();
	public void AnimationEvent_TurretEnableLeftHandIk() => m_BoundController?.AnimationEvent_TurretEnableLeftHandIk();
	public void AnimationEvent_TurretHandToHandle() => m_BoundController?.AnimationEvent_TurretHandToHandle();
	public void AnimationEvent_TurretHandleYankDown() => m_BoundController?.AnimationEvent_TurretHandleYankDown();
	public void AnimationEvent_TurretHandleFirstReturnUp() => m_BoundController?.AnimationEvent_TurretHandleFirstReturnUp();
	public void AnimationEvent_TurretHandleSecondYankDown() => m_BoundController?.AnimationEvent_TurretHandleSecondYankDown();
	public void AnimationEvent_TurretHandleSecondReturnUp() => m_BoundController?.AnimationEvent_TurretHandleSecondReturnUp();
	public void AnimationEvent_TurretReleaseHandleIk() => m_BoundController?.AnimationEvent_TurretReleaseHandleIk();
	public void AnimationEvent_TurretFinishReload() => m_BoundController?.AnimationEvent_TurretFinishReload();
	// Legacy clip aliases
	public void AnimationEvent_TurretHandleReturnUp() => m_BoundController?.AnimationEvent_TurretHandleReturnUp();
	#endregion
}
