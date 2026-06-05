using UnityEngine;

public static class WeaponVfxUtility
{
	public static WeaponVfxProfile GetCurrentProfile(UnitWeaponRuntime _runtime)
	{
		WeaponDefinition weaponDefinition = _runtime != null ? _runtime.CurrentWeaponDefinition : null;
		return weaponDefinition != null ? weaponDefinition.VfxProfile : null;
	}

	public static bool HasSuppressor(UnitWeaponRuntime _runtime)
	{
		WeaponRuntimeState state = _runtime != null ? _runtime.RuntimeState : null;
		WeaponAttachmentDefinition[] attachments = state != null ? state.EquippedAttachments : null;
		if (attachments == null)
			return false;

		for (int i = 0; i < attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = attachments[i];
			if (attachment != null && attachment.AttachmentType == WeaponAttachmentType.Suppressor)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Та же точка и направление, что у <see cref="UnitWeaponShellEjection"/>:
	/// ShellEject.position + ShellEject.forward, иначе barrel.position и -barrel.right.
	/// </summary>
	public static bool TryGetShellEjectionPose(
		EquippedWeapon _weapon,
		out Vector3 _position,
		out Vector3 _direction)
	{
		_position = Vector3.zero;
		_direction = Vector3.right;
		if (_weapon == null)
			return false;

		Transform barrel = _weapon.BarrelTransform;
		if (barrel == null)
			return false;

		Transform eject = _weapon.ShellEjectTransform;
		_position = eject != null ? eject.position : barrel.position;

		Vector3 dir = eject != null ? eject.forward : (-barrel.right);
		float dirLen = dir.sqrMagnitude;
		if (dirLen > 1e-6f)
			_direction = dir / Mathf.Sqrt(dirLen);
		else
			_direction = Vector3.right;

		return true;
	}

	public static Quaternion BuildParticleShellRotation(WeaponVfxProfile _profile, Vector3 _worldDirection)
	{
		Vector3 worldDirection = _worldDirection.sqrMagnitude > 1e-6f
			? _worldDirection.normalized
			: Vector3.right;
		Vector3 prefabAxis = _profile != null && _profile.ShellPrefabEjectionAxis.sqrMagnitude > 1e-6f
			? _profile.ShellPrefabEjectionAxis.normalized
			: Vector3.right;

		Quaternion rotation = Quaternion.FromToRotation(prefabAxis, worldDirection);
		if (_profile != null && _profile.ShellLocalEulerOffset.sqrMagnitude > 1e-6f)
			rotation *= Quaternion.Euler(_profile.ShellLocalEulerOffset);

		return rotation;
	}

	public static void PlayParticleSystems(GameObject _root)
	{
		if (_root == null)
			return;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			system.Clear(true);
			system.Play(true);
		}
	}

	public static void PlayShellParticles(GameObject _instance)
	{
		ParticleSystem[] systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			system.Clear(true);
			system.Play(true);
		}
	}

	public static void PrepareShellParticleInstance(GameObject _instance)
	{
		ParticleSystem[] systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem.MainModule main = systems[i].main;
			main.loop = false;
			main.playOnAwake = false;
			main.stopAction = ParticleSystemStopAction.None;
		}
	}

	public static bool IsParticleRootAlive(GameObject _instance)
	{
		ParticleSystem[] systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			if (systems[i].IsAlive(true))
				return true;
		}

		return false;
	}

}
