using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(UnitStabilizedUnconsciousPoseController))]
public sealed class UnitConsciousness : MonoBehaviour
{
	#region Events
	public event Action<bool> ConsciousnessChanged;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private bool m_IsConscious = true;
	#endregion

	#region Public Properties
	public bool IsConscious => m_IsConscious;
	public bool IsTargetable => m_IsConscious;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		if (m_RagdollController != null && m_RagdollController.IsRagdollActive != !m_IsConscious)
			m_RagdollController.SetRagdollActive(!m_IsConscious);
	}
	#endregion

	#region Public Methods
	public void EnterUnconscious()
	{
		EnterUnconscious(Vector3.zero);
	}

	public void EnterUnconscious(Vector3 _impulse)
	{
		SetConscious(false, _impulse);
	}

	public void EnterUnconscious(DamageHitInfo _hitInfo, UnitRagdollController.RagdollFallProfile _fallProfile)
	{
		SetConscious(false, _hitInfo, _fallProfile);
	}

	public void WakeUp()
	{
		SetConscious(true, Vector3.zero);
	}

	public static bool IsTargetableTarget(Transform _target)
	{
		if (_target == null)
			return false;

		UnitConsciousness consciousness = _target.GetComponent<UnitConsciousness>();
		return consciousness == null || consciousness.IsTargetable;
	}
	#endregion

	#region Private Methods
	private void SetConscious(bool _isConscious, Vector3 _impulse)
	{
		if (m_IsConscious == _isConscious)
			return;

		ResolveReferences();
		m_IsConscious = _isConscious;

		if (m_RagdollController != null)
			m_RagdollController.SetRagdollActive(!_isConscious, _impulse);

		ConsciousnessChanged?.Invoke(m_IsConscious);
	}

	private void SetConscious(bool _isConscious, DamageHitInfo _hitInfo, UnitRagdollController.RagdollFallProfile _fallProfile)
	{
		if (m_IsConscious == _isConscious)
			return;

		ResolveReferences();
		m_IsConscious = _isConscious;

		if (m_RagdollController != null)
			m_RagdollController.SetRagdollActive(!_isConscious, _hitInfo, _fallProfile);

		ConsciousnessChanged?.Invoke(m_IsConscious);
	}

	private void ResolveReferences()
	{
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
	}
	#endregion

#if UNITY_EDITOR
	#region Editor Debug
	[ContextMenu("Debug: Enter Unconscious")]
	private void DebugEnterUnconscious()
	{
		EnterUnconscious();
	}

	[ContextMenu("Debug: Wake Up")]
	private void DebugWakeUp()
	{
		WakeUp();
	}
	#endregion
#endif
}
