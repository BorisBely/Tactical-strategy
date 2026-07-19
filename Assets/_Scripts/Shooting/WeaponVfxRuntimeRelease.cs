using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Общий lifecycle для pooled VFX: budget release + optional particle-alive wait.
/// </summary>
public static class WeaponVfxRuntimeRelease
{
	#region Public Methods
	public static void StartRelease(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		CombatVfxBudgetService.Category _category,
		float _minSeconds,
		bool _waitForParticles = false)
	{
		if (_pool == null || _instance == null)
		{
			CombatVfxBudgetService.Release(_category);
			return;
		}

		MonoBehaviour host = ResolveHost(_instance);
		if (host == null)
		{
			ReleaseImmediate(_pool, _instance, _category);
			return;
		}

		host.StartCoroutine(ReleaseRoutine(_pool, _instance, _category, _minSeconds, _waitForParticles));
	}

	public static void StartSmokeCloudRelease(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		float _activeSeconds,
		float _audioFadeOutSeconds = 3f,
		float _dissipateSeconds = 10f)
	{
		if (_pool == null || _instance == null)
		{
			CombatVfxBudgetService.Release(CombatVfxBudgetService.Category.SmokeCloud);
			return;
		}

		MonoBehaviour host = ResolveHost(_instance);
		if (host == null)
		{
			ReleaseImmediate(_pool, _instance, CombatVfxBudgetService.Category.SmokeCloud);
			return;
		}

		host.StartCoroutine(SmokeCloudReleaseRoutine(
			_pool,
			_instance,
			_activeSeconds,
			_audioFadeOutSeconds,
			_dissipateSeconds));
	}

	public static void StartDecalRelease(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		float _seconds)
	{
		if (_pool == null || _instance == null)
		{
			CombatVfxBudgetService.Release(CombatVfxBudgetService.Category.Decal);
			return;
		}

		CombatVfxBudgetService.RegisterActiveDecal(_instance, _pool);
		MonoBehaviour host = ResolveHost(_instance);
		if (host == null)
		{
			ReleaseDecalImmediate(_pool, _instance);
			return;
		}

		host.StartCoroutine(DecalReleaseRoutine(_pool, _instance, _seconds));
	}
	#endregion

	#region Private Methods
	private static IEnumerator ReleaseRoutine(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		CombatVfxBudgetService.Category _category,
		float _minSeconds,
		bool _waitForParticles)
	{
		if (_minSeconds > 0f)
			yield return new WaitForSeconds(_minSeconds);

		if (_waitForParticles)
		{
			while (_instance != null && WeaponVfxUtility.IsParticleRootAlive(_instance))
				yield return null;
		}

		ReleaseImmediate(_pool, _instance, _category);
	}

	private static IEnumerator SmokeCloudReleaseRoutine(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		float _activeSeconds,
		float _audioFadeOutSeconds,
		float _dissipateSeconds)
	{
		// Полная эмиссия до истечения таймера гранаты.
		if (_activeSeconds > 0f)
			yield return new WaitForSeconds(_activeSeconds);

		if (_instance != null)
		{
			if (_instance.TryGetComponent(out GrenadeSmokeAudioLoop smokeAudio))
				smokeAudio.FadeOutAndStop();

			WeaponVfxUtility.StopParticleSystems(_instance, _clear: false);
		}

		// Ждём, пока уже выпущенные частицы доживут.
		float lingerSeconds = Mathf.Max(10f, _audioFadeOutSeconds + 8f);
		float elapsed = 0f;
		while (_instance != null && elapsed < lingerSeconds)
		{
			if (!WeaponVfxUtility.IsParticleRootAlive(_instance))
				break;

			elapsed += Time.deltaTime;
			yield return null;
		}

		ReleaseImmediate(_pool, _instance, CombatVfxBudgetService.Category.SmokeCloud);
	}

	private static IEnumerator DecalReleaseRoutine(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		float _seconds)
	{
		if (_seconds > 0f)
			yield return new WaitForSeconds(_seconds);

		if (CombatVfxBudgetService.IsDecalRegistered(_instance))
			ReleaseDecalImmediate(_pool, _instance);
	}

	private static void ReleaseImmediate(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		CombatVfxBudgetService.Category _category)
	{
		if (_instance != null)
			_pool.Release(_instance);

		CombatVfxBudgetService.Release(_category);
	}

	private static void ReleaseDecalImmediate(ObjectPool<GameObject> _pool, GameObject _instance)
	{
		if (!CombatVfxBudgetService.TryUnregisterActiveDecal(_instance))
			return;

		if (_instance != null)
			_pool.Release(_instance);

		CombatVfxBudgetService.Release(CombatVfxBudgetService.Category.Decal);
	}

	private static MonoBehaviour ResolveHost(GameObject _instance)
	{
		WeaponVfxReleaseHost host = _instance.GetComponent<WeaponVfxReleaseHost>();
		if (host != null)
			return host;

		host = _instance.AddComponent<WeaponVfxReleaseHost>();
		return host;
	}
	#endregion
}

/// <summary>Minimal host so pooled FX can run release coroutines without a unit reference.</summary>
[DisallowMultipleComponent]
internal sealed class WeaponVfxReleaseHost : MonoBehaviour
{
}
