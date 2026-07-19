using System.Collections;
using UnityEngine;

public static class WeaponVfxUtility
{
	#region Private Fields
	private static Camera s_CachedMainCamera;
	#endregion

	#region Public Methods
	public static WeaponVfxProfile GetCurrentProfile(UnitWeaponRuntime _runtime)
	{
		WeaponDefinition weaponDefinition = _runtime != null ? _runtime.CurrentWeaponDefinition : null;
		return weaponDefinition != null ? weaponDefinition.VfxProfile : null;
	}

	/// <summary>
	/// True, если <see cref="UnitWeaponImpactVfx"/> реально проиграет звук попадания по поверхности
	/// для этой трассы (тело/броня — без surface impact audio).
	/// </summary>
	public static bool WillPlaySurfaceImpactAudio(
		UnitWeaponRuntime _runtime,
		Collider _hitCollider,
		WeaponShotImpactVfxKind _impactVfxKind)
	{
		if (_hitCollider == null)
			return false;

		if (_impactVfxKind is WeaponShotImpactVfxKind.ArmorDeflect or WeaponShotImpactVfxKind.Flesh)
			return false;

		WeaponVfxProfile profile = GetCurrentProfile(_runtime);
		if (profile == null || !profile.EnableImpactAudio)
			return false;

		if (!profile.IsImpactSurfaceLayer(_hitCollider.gameObject.layer))
			return false;

		if (!profile.TryResolveImpactSurface(_hitCollider, out WeaponImpactSurfaceSet surface) || surface == null)
			return false;

		return surface.HasAnyImpactSound();
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

	public static bool ShouldUsePhysicalShellEjection(WeaponVfxProfile _profile, Vector3 _shellWorldPosition)
	{
		if (_profile == null)
			return true;

		if (_profile.UsePhysicalShellEjection)
			return true;

		if (!_profile.UseHybridShellEjection)
			return false;

		return IsWithinNearCameraDetailDistance(_profile, _shellWorldPosition);
	}

	/// <summary>Particle FX: чистый Particle или Hybrid на дистанции от камеры.</summary>
	public static bool ShouldUseParticleShellEjection(WeaponVfxProfile _profile, Vector3 _shellWorldPosition)
	{
		if (_profile == null)
			return false;

		if (_profile.UseParticleShellEjection)
			return true;

		return _profile.UseHybridShellEjection && !ShouldUsePhysicalShellEjection(_profile, _shellWorldPosition);
	}

	public static Camera ResolveActiveCamera()
	{
		if (s_CachedMainCamera != null && s_CachedMainCamera.isActiveAndEnabled)
			return s_CachedMainCamera;

		s_CachedMainCamera = Camera.main;
		if (s_CachedMainCamera != null && s_CachedMainCamera.isActiveAndEnabled)
			return s_CachedMainCamera;

		Camera[] cameras = Camera.allCameras;
		for (int i = 0; i < cameras.Length; i++)
		{
			Camera camera = cameras[i];
			if (camera != null && camera.isActiveAndEnabled)
			{
				s_CachedMainCamera = camera;
				return s_CachedMainCamera;
			}
		}

		s_CachedMainCamera = null;
		return null;
	}

	public static void InvalidateActiveCameraCache()
	{
		s_CachedMainCamera = null;
	}

	public static bool IsWithinDistance(Vector3 _worldPosition, float _maxDistanceMeters)
	{
		if (_maxDistanceMeters <= 0f)
			return false;

		Camera camera = ResolveActiveCamera();
		if (camera == null)
			return false;

		return (_worldPosition - camera.transform.position).sqrMagnitude <= _maxDistanceMeters * _maxDistanceMeters;
	}

	/// <summary>
	/// Near-camera detail LOD: visual kick, цикл затвора, физические гильзы в Hybrid.
	/// Порог — <see cref="WeaponVfxProfile.HybridPhysicalShellDistanceMeters"/>.
	/// </summary>
	public static bool IsWithinNearCameraDetailDistance(WeaponVfxProfile _profile, Vector3 _worldPosition)
	{
		float distance = _profile != null
			? Mathf.Max(0f, _profile.HybridPhysicalShellDistanceMeters)
			: 12f;
		return IsWithinDistance(_worldPosition, distance);
	}

	public static bool IsWithinEffectDistance(Vector3 _worldPosition, float _maxDistanceMeters)
	{
		return IsWithinDistance(_worldPosition, _maxDistanceMeters);
	}

	public static WeaponVfxQualityTier ResolveEffectQualityTier(
		WeaponVfxProfile _profile,
		Vector3 _worldPosition,
		float _maxDistanceMeters,
		float _nearDistanceMeters = -1f,
		float _midDistanceMeters = -1f)
	{
		if (_maxDistanceMeters <= 0f || !IsWithinEffectDistance(_worldPosition, _maxDistanceMeters))
			return WeaponVfxQualityTier.Skip;

		float nearDistance = _nearDistanceMeters >= 0f
			? _nearDistanceMeters
			: _profile != null ? _profile.EffectNearQualityDistanceMeters : 15f;
		float midDistance = _midDistanceMeters >= 0f
			? _midDistanceMeters
			: _profile != null ? _profile.EffectMidQualityDistanceMeters : 35f;

		nearDistance = Mathf.Max(0f, nearDistance);
		midDistance = Mathf.Clamp(Mathf.Max(nearDistance, midDistance), 0f, _maxDistanceMeters);

		Camera camera = ResolveActiveCamera();
		if (camera == null)
			return WeaponVfxQualityTier.Skip;

		float sqrDistance = (_worldPosition - camera.transform.position).sqrMagnitude;
		if (sqrDistance <= nearDistance * nearDistance)
			return WeaponVfxQualityTier.Full;

		if (sqrDistance <= midDistance * midDistance)
			return WeaponVfxQualityTier.Reduced;

		return WeaponVfxQualityTier.Skip;
	}

	public static void ApplyParticleQualityTier(GameObject _root, WeaponVfxProfile _profile, WeaponVfxQualityTier _tier)
	{
		if (_root == null || _profile == null || _tier != WeaponVfxQualityTier.Reduced)
			return;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			ParticleSystem.MainModule main = system.main;
			int maxParticles = main.maxParticles;
			if (maxParticles > 0)
			{
				main.maxParticles = Mathf.Max(
					1,
					Mathf.RoundToInt(maxParticles * _profile.ReducedMaxParticlesMultiplier));
			}
		}

		Transform rootTransform = _root.transform;
		rootTransform.localScale *= _profile.ReducedParticleScaleMultiplier;
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
			ParticleSystem system = systems[i];
			ParticleSystem.MainModule main = system.main;
			main.loop = false;
			main.playOnAwake = false;
			main.stopAction = ParticleSystemStopAction.None;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.scalingMode = ParticleSystemScalingMode.Hierarchy;
			system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			system.Clear(true);
		}
	}

	public static void PrepareBodyImpactParticleInstance(GameObject _instance)
	{
		PrepareParticleInstance(_instance, _forceNonLooping: true);
	}

	/// <summary>
	/// Только аудио-компонент для пула. Параметры ParticleSystem не трогаем.
	/// </summary>
	public static void PrepareSmokeParticleInstance(GameObject _instance)
	{
		if (_instance == null)
			return;

		if (!_instance.TryGetComponent(out GrenadeSmokeAudioLoop _))
			_instance.AddComponent<GrenadeSmokeAudioLoop>();
	}

	public static void ApplySmokeSpawnTransform(GameObject _instance, GameObject _prefab, Vector3 _position)
	{
		if (_instance == null)
			return;

		Transform t = _instance.transform;
		t.position = _position;

		if (_prefab == null)
			return;

		Transform prefabTransform = _prefab.transform;
		t.localRotation = prefabTransform.localRotation;
		t.localScale = prefabTransform.localScale;
	}

	public static void PlaySmokeParticleSystems(GameObject _root)
	{
		if (_root == null)
			return;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			if (system == null)
				continue;

			if (!system.isPlaying)
				system.Play(true);
		}
	}

	public static void StopParticleSystems(GameObject _root, bool _clear = true)
	{
		if (_root == null)
			return;

		ParticleSystemStopBehavior stopBehavior = _clear
			? ParticleSystemStopBehavior.StopEmittingAndClear
			: ParticleSystemStopBehavior.StopEmitting;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			if (systems[i] == null)
				continue;

			systems[i].Stop(true, stopBehavior);
			if (_clear)
				systems[i].Clear(true);
		}
	}

	/// <summary>
	/// После таймера активной эмиссии — плавно снижает rateOverTimeMultiplier, затем StopEmitting.
	/// </summary>
	public static IEnumerator DissipateSmokeCloud(GameObject _root, float _dissipateSeconds)
	{
		if (_root == null)
			yield break;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		if (systems == null || systems.Length == 0)
			yield break;

		float duration = Mathf.Max(0.75f, _dissipateSeconds);
		float elapsed = 0f;
		while (_root != null && elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			float rateScale = 1f - (t * t);

			for (int i = 0; i < systems.Length; i++)
			{
				ParticleSystem system = systems[i];
				if (system == null)
					continue;

				ParticleSystem.EmissionModule emission = system.emission;
				emission.rateOverTimeMultiplier = rateScale;
			}

			yield return null;
		}

		if (_root == null)
			yield break;

		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			if (system == null)
				continue;

			ParticleSystem.EmissionModule emission = system.emission;
			emission.rateOverTimeMultiplier = 0f;
			system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		}
	}

	public static void ResetSmokeEmissionMultipliers(GameObject _root)
	{
		if (_root == null)
			return;

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			if (systems[i] == null)
				continue;

			ParticleSystem.EmissionModule emission = systems[i].emission;
			emission.rateOverTimeMultiplier = 1f;
		}
	}

	/// <summary>
	/// Подготовка pooled VFX. Для дымовых облаков оставляем loop/duration как в префабе.
	/// </summary>
	public static void PrepareParticleInstance(GameObject _instance, bool _forceNonLooping)
	{
		if (_instance == null)
			return;

		ParticleSystem[] systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem.MainModule main = systems[i].main;
			if (_forceNonLooping)
				main.loop = false;
			main.playOnAwake = false;
			main.stopAction = ParticleSystemStopAction.None;
			main.scalingMode = ParticleSystemScalingMode.Hierarchy;
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
	#endregion
}
