using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр мишеней полигона для <see cref="UnitVision"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShootingRangeTargetRegistry : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private List<ShootingRangeTarget> m_Targets = new List<ShootingRangeTarget>(16);
	private readonly List<ShootingRangeTarget> m_Scratch = new List<ShootingRangeTarget>(16);
	#endregion

	#region Public Methods
	public void Register(ShootingRangeTarget _target)
	{
		if (_target == null || m_Targets.Contains(_target))
			return;

		m_Targets.Add(_target);
	}

	public void Unregister(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

		m_Targets.Remove(_target);
	}

	public void GetActiveTargets(List<ShootingRangeTarget> _outBuffer)
	{
		_outBuffer.Clear();
		for (int i = 0; i < m_Targets.Count; i++)
		{
			ShootingRangeTarget target = m_Targets[i];
			if (target != null && target.IsAvailableForTargeting)
				_outBuffer.Add(target);
		}
	}

	public IReadOnlyList<ShootingRangeTarget> GetAllTargets()
	{
		return m_Targets;
	}

	public void NotifyTargetEliminated(ShootingRangeTarget _target)
	{
		if (_target == null)
			return;

#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		UnitVision[] visions = FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
		{
			UnitVision vision = visions[i];
			if (vision == null || !vision.IsTrackingTarget(_target.transform))
				continue;

			vision.ClearVisibleTargetAndWaitForNextScan();
		}
	}
	#endregion
}
