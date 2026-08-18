using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальный пул боевых SFX с жёсткими квотами голосов:
/// ~70% выстрелы, ~10% попадания, ~20% перезарядка / whiz / прочее.
/// Один слот = один клип (без PlayOneShot), чтобы не было наложений и ложных «свободных» голосов.
/// </summary>
public static class CombatAudioManager
{
	#region Constants
	private const int c_VoicePoolSize = 48;
	private const int c_GunshotVoiceBudget = 34;
	private const int c_ImpactVoiceBudget = 5;
	private const int c_OtherVoiceBudget = 9;

	private const float c_ImpactMaxDistanceMeters = 40f;
	private const float c_GunshotMediumDistanceMeters = 50f;
	private const float c_GunshotFarDistanceMeters = 80f;
	private const float c_GunshotMinIntervalSeconds = 0.05f;
	private const float c_RecentGunshotWindowSeconds = 0.16f;
	private const float c_MinAudibleVolume = 0.015f;
	private const float c_SelectedUnitPriorityBonus = 400f;
	private const int c_PriorityStealMargin = 25;
	private const float c_DefaultSpatialMinDistance = 1f;
	private const float c_DefaultReloadMaxDistance = 45f;
	private const float c_NonFireVolumeMultiplier = 0.65f;
	private const float c_NonFireSpatialMinDistance = 2.5f;
	private const float c_RolloffMinAudibleVolume = 0.08f;
	private const float c_RolloffAttenuationPower = 1.35f;
	private const int c_RolloffCurveKeyCount = 9;

	private const int c_TierGunshot = 400;
	private const int c_TierRocketLauncher = 500;
	private const int c_TierImpact = 300;
	private const int c_TierCombatSecondary = 200;
	private const int c_TierNonFire = 100;
	private const int c_TierScoreMultiplier = 1000;
	private const int c_RocketLauncherPriorityBonus = 8000;

	private const int c_UnityPriorityGunshot = 0;
	private const int c_UnityPriorityImpact = 64;
	private const int c_UnityPriorityOther = 128;
	#endregion

	#region Nested Types
	public enum Category
	{
		Gunshot = 0,
		Impact = 1,
		CombatSecondary = 2,
		NonFire = 3,
		RocketLauncher = 4,
	}

	private enum VoiceGroup
	{
		Gunshot = 0,
		Impact = 1,
		Other = 2,
	}

	private struct VoiceSlot
	{
		public AudioSource Source;
		public Category Category;
		public VoiceGroup Group;
		public int Priority;
		public EntityId OwnerEntityId;
		public EntityId WeaponSignatureId;
		public float EndUnscaledTime;
		public bool IsBulletWhiz;
	}

	private struct RecentGunshotRecord
	{
		public EntityId OwnerId;
		public EntityId WeaponSignatureId;
		public float Time;
	}
	#endregion

	#region Static Fields
	private static VoiceSlot[] s_VoiceSlots;
	private static Transform s_PoolRoot;
	private static AnimationCurve s_RolloffCurve;
	private static readonly Dictionary<EntityId, float> s_LastGunshotTimeByOwner = new Dictionary<EntityId, float>(64);
	private static readonly Dictionary<EntityId, int> s_GunshotSkipCounterByWeaponSignature = new Dictionary<EntityId, int>(32);
	private static readonly List<RecentGunshotRecord> s_RecentGunshots = new List<RecentGunshotRecord>(128);
	private static readonly HashSet<EntityId> s_TempDuplicateOwnerIds = new HashSet<EntityId>(8);
	#endregion

	#region Public Methods
	public static bool TryPlayGunshot(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _pitch,
		float _maxDistance,
		Transform _ownerOrNull,
		float _minDistance = c_DefaultSpatialMinDistance,
		EntityId _weaponSignatureId = default)
	{
		if (!TryValidatePlayRequest(_clip, _volume, _position, _maxDistance, Category.Gunshot, out float estimatedVolume))
			return false;

		EntityId ownerId = _ownerOrNull != null ? _ownerOrNull.GetEntityId() : default;
		EntityId weaponSignatureId = _weaponSignatureId.IsValid() ? _weaponSignatureId : _clip.GetEntityId();
		if (!TryPassGunshotThrottle(_ownerOrNull, _position, ownerId, weaponSignatureId))
			return false;

		int priority = ComputePriority(
			_position,
			_ownerOrNull,
			Category.Gunshot,
			estimatedVolume,
			ownerId,
			weaponSignatureId);

		bool played = TryPlayInternal(
			_clip,
			_position,
			_volume,
			_pitch,
			_maxDistance,
			_minDistance,
			Category.Gunshot,
			priority,
			ownerId,
			weaponSignatureId,
			_nonSpatial: false);

		if (played)
		{
			RecordRecentGunshot(ownerId, weaponSignatureId);
			s_LastGunshotTimeByOwner[ownerId] = Time.unscaledTime;
		}

		return played;
	}

	public static bool TryPlayImpact(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _maxDistance = c_ImpactMaxDistanceMeters)
	{
		float clampedMaxDistance = Mathf.Min(_maxDistance, c_ImpactMaxDistanceMeters);
		if (!TryValidatePlayRequest(_clip, _volume, _position, clampedMaxDistance, Category.Impact, out float estimatedVolume))
			return false;

		int priority = ComputePriority(_position, null, Category.Impact, estimatedVolume, default, default);
		return TryPlayInternal(
			_clip,
			_position,
			_volume * c_NonFireVolumeMultiplier,
			1f,
			clampedMaxDistance,
			c_NonFireSpatialMinDistance,
			Category.Impact,
			priority,
			default,
			default,
			_nonSpatial: false);
	}

	public static bool TryPlayReload(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		Transform _ownerOrNull = null,
		float _maxDistance = c_DefaultReloadMaxDistance)
	{
		if (_clip == null || _volume <= 0f)
			return false;

		float scaledVolume = c_NonFireVolumeMultiplier * _volume;
		if (scaledVolume <= 0f)
			return false;

		float maxDistance = Mathf.Max(c_NonFireSpatialMinDistance + 0.01f, _maxDistance);
		if (!TryValidatePlayRequest(_clip, scaledVolume, _position, maxDistance, Category.CombatSecondary, out float estimatedVolume))
			return false;

		EntityId ownerId = _ownerOrNull != null ? _ownerOrNull.GetEntityId() : default;
		int priority = ComputePriority(_position, _ownerOrNull, Category.CombatSecondary, estimatedVolume, ownerId, default);
		return TryPlayInternal(
			_clip,
			_position,
			scaledVolume,
			1f,
			maxDistance,
			c_NonFireSpatialMinDistance,
			Category.CombatSecondary,
			priority,
			ownerId,
			default,
			_nonSpatial: false);
	}

	public static bool TryPlayBulletWhiz(AudioClip _clip, float _volume, float _pitch = 1f)
	{
		if (_clip == null || _volume <= c_MinAudibleVolume)
			return false;

		Vector3 listenerPosition = GetListenerPosition();
		int priority = ComputePriority(listenerPosition, null, Category.CombatSecondary, _volume, default, default);
		return TryPlayInternal(
			_clip,
			listenerPosition,
			_volume,
			_pitch,
			_maxDistance: 1f,
			_minDistance: 0.01f,
			Category.CombatSecondary,
			priority,
			default,
			default,
			_nonSpatial: true,
			_isBulletWhiz: true);
	}

	public static bool TryPlayNonFire(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _maxDistance = 40f,
		Transform _ownerOrNull = null)
	{
		if (!TryValidatePlayRequest(_clip, _volume, _position, _maxDistance, Category.NonFire, out float estimatedVolume))
			return false;

		float scaledVolume = _volume * c_NonFireVolumeMultiplier;
		EntityId ownerId = _ownerOrNull != null ? _ownerOrNull.GetEntityId() : default;
		int priority = ComputePriority(_position, _ownerOrNull, Category.NonFire, estimatedVolume, ownerId, default);
		return TryPlayInternal(
			_clip,
			_position,
			scaledVolume,
			1f,
			_maxDistance,
			c_NonFireSpatialMinDistance,
			Category.NonFire,
			priority,
			ownerId,
			default,
			_nonSpatial: false);
	}

	/// <summary>
	/// Взрывы гранат: высокий приоритет, без NonFire-attenuation; при перегрузке вытесняет слабые голоса.
	/// </summary>
	public static bool TryPlayExplosion(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _maxDistance = 90f,
		Transform _ownerOrNull = null)
	{
		if (_clip == null || _volume <= 0f)
			return false;

		float maxDistance = Mathf.Max(5f, _maxDistance);
		float distance = GetListenerDistance(_position);
		if (distance >= maxDistance)
			return false;

		float estimatedVolume = EstimateVolumeAtListener(_position, _volume, maxDistance);
		if (estimatedVolume < c_MinAudibleVolume * 0.35f)
			return false;

		EntityId ownerId = _ownerOrNull != null ? _ownerOrNull.GetEntityId() : default;
		int priority = ComputeRocketLauncherPriority(_position, _ownerOrNull, estimatedVolume, ownerId);
		return TryPlayInternal(
			_clip,
			_position,
			_volume,
			1f,
			maxDistance,
			c_DefaultSpatialMinDistance,
			Category.RocketLauncher,
			priority,
			ownerId,
			default,
			_nonSpatial: false,
			_isBulletWhiz: false,
			_forceMaximumPriority: true);
	}

	/// <summary>
	/// Гранатомёт: fire / flyby / explosion — всегда с максимальным приоритетом, без NonFire- attenuation.
	/// </summary>
	public static bool TryPlayRocketLauncher(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _maxDistance = 120f,
		Transform _ownerOrNull = null,
		float _pitch = 1f,
		bool _nonSpatial = false)
	{
		if (_clip == null || _volume <= 0f)
			return false;

		float maxDistance = _nonSpatial ? 1f : Mathf.Max(5f, _maxDistance);
		float minDistance = _nonSpatial ? 0.01f : c_DefaultSpatialMinDistance;
		float estimatedVolume = _nonSpatial
			? _volume
			: EstimateVolumeAtListener(_position, _volume, maxDistance);

		EntityId ownerId = _ownerOrNull != null ? _ownerOrNull.GetEntityId() : default;
		int priority = ComputeRocketLauncherPriority(_position, _ownerOrNull, estimatedVolume, ownerId);
		return TryPlayInternal(
			_clip,
			_position,
			_volume,
			_pitch,
			maxDistance,
			minDistance,
			Category.RocketLauncher,
			priority,
			ownerId,
			default,
			_nonSpatial: _nonSpatial,
			_isBulletWhiz: false,
			_forceMaximumPriority: true);
	}
	#endregion

	#region Private Methods
	private static bool TryValidatePlayRequest(
		AudioClip _clip,
		float _volume,
		Vector3 _position,
		float _maxDistance,
		Category _category,
		out float _estimatedVolumeAtListener)
	{
		_estimatedVolumeAtListener = 0f;
		if (_clip == null || _volume <= 0f || _maxDistance <= 0f)
			return false;

		if (_category == Category.Impact)
		{
			float impactDistance = GetListenerDistance(_position);
			if (impactDistance > c_ImpactMaxDistanceMeters)
				return false;
		}

		_estimatedVolumeAtListener = EstimateVolumeAtListener(_position, _volume, _maxDistance);
		return _estimatedVolumeAtListener >= c_MinAudibleVolume;
	}

	private static bool TryPassGunshotThrottle(
		Transform _ownerOrNull,
		Vector3 _position,
		EntityId _ownerId,
		EntityId _weaponSignatureId)
	{
		bool isSelected = IsOwnerSelected(_ownerOrNull);
		float now = Time.unscaledTime;

		if (_ownerId.IsValid() &&
		    s_LastGunshotTimeByOwner.TryGetValue(_ownerId, out float lastTime) &&
		    now - lastTime < c_GunshotMinIntervalSeconds)
		{
			return isSelected;
		}

		if (isSelected || !_weaponSignatureId.IsValid())
			return true;

		int gunshotPlaying = CountPlayingVoiceGroup(VoiceGroup.Gunshot);
		if (gunshotPlaying < c_GunshotVoiceBudget)
			return true;

		int duplicateOwnerCount = CountRecentDuplicateWeaponOwners(_weaponSignatureId, _ownerId);
		if (duplicateOwnerCount <= 0)
			return true;

		float listenerDistance = GetListenerDistance(_position);
		int skipRate = ResolveDuplicateWeaponSkipRate(duplicateOwnerCount, listenerDistance);
		if (skipRate <= 1)
			return true;

		if (!s_GunshotSkipCounterByWeaponSignature.TryGetValue(_weaponSignatureId, out int counter))
			counter = 0;

		counter++;
		s_GunshotSkipCounterByWeaponSignature[_weaponSignatureId] = counter;
		return counter % skipRate == 0;
	}

	private static int ResolveDuplicateWeaponSkipRate(int _duplicateOwnerCount, float _listenerDistance)
	{
		int skipRate = Mathf.Clamp(_duplicateOwnerCount, 2, 3);
		if (_listenerDistance > c_GunshotFarDistanceMeters)
			skipRate = 3;
		else if (_listenerDistance > c_GunshotMediumDistanceMeters)
			skipRate = Mathf.Max(skipRate, 2);

		return skipRate;
	}

	private static void RecordRecentGunshot(EntityId _ownerId, EntityId _weaponSignatureId)
	{
		float now = Time.unscaledTime;
		PruneRecentGunshots(now);
		s_RecentGunshots.Add(new RecentGunshotRecord
		{
			OwnerId = _ownerId,
			WeaponSignatureId = _weaponSignatureId,
			Time = now,
		});
	}

	private static void PruneRecentGunshots(float _now)
	{
		for (int i = s_RecentGunshots.Count - 1; i >= 0; i--)
		{
			if (_now - s_RecentGunshots[i].Time > c_RecentGunshotWindowSeconds)
				s_RecentGunshots.RemoveAt(i);
		}
	}

	private static int CountRecentDuplicateWeaponOwners(EntityId _weaponSignatureId, EntityId _excludeOwnerId)
	{
		float now = Time.unscaledTime;
		PruneRecentGunshots(now);

		s_TempDuplicateOwnerIds.Clear();
		for (int i = 0; i < s_RecentGunshots.Count; i++)
		{
			RecentGunshotRecord record = s_RecentGunshots[i];
			if (record.WeaponSignatureId != _weaponSignatureId)
				continue;

			EntityId ownerId = record.OwnerId;
			if (!ownerId.IsValid() || ownerId == _excludeOwnerId)
				continue;

			s_TempDuplicateOwnerIds.Add(ownerId);
		}

		return s_TempDuplicateOwnerIds.Count;
	}

	private static bool TryPlayInternal(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		float _pitch,
		float _maxDistance,
		float _minDistance,
		Category _category,
		int _priority,
		EntityId _ownerEntityId,
		EntityId _weaponSignatureId,
		bool _nonSpatial,
		bool _isBulletWhiz = false,
		bool _forceMaximumPriority = false)
	{
		EnsureVoicePool();
		if (s_VoiceSlots == null || s_VoiceSlots.Length == 0)
			return false;

		VoiceGroup group = ResolveVoiceGroup(_category);
		if (!TryAcquireVoiceSlot(group, _priority, out int slotIndex, _forceMaximumPriority))
			return false;

		ref VoiceSlot slot = ref s_VoiceSlots[slotIndex];
		AudioSource source = slot.Source;
		if (source == null)
			return false;

		if (_nonSpatial)
			ConfigureNonSpatial(source);
		else
			ConfigureSpatial(source, _minDistance, _maxDistance);

		source.priority = ResolveUnityPriority(_category);
		source.transform.position = _position;
		source.pitch = Mathf.Clamp(_pitch, 0.1f, 3f);
		source.volume = Mathf.Clamp01(_volume);
		source.clip = _clip;
		source.loop = false;
		source.Play();

		float duration = _clip.length / Mathf.Max(0.1f, Mathf.Abs(source.pitch));
		slot.Category = _category;
		slot.Group = group;
		slot.Priority = _priority;
		slot.OwnerEntityId = _ownerEntityId;
		slot.WeaponSignatureId = _weaponSignatureId;
		slot.EndUnscaledTime = Time.unscaledTime + duration;
		slot.IsBulletWhiz = _isBulletWhiz;
		return true;
	}

	private static VoiceGroup ResolveVoiceGroup(Category _category)
	{
		switch (_category)
		{
			case Category.Gunshot:
			case Category.RocketLauncher:
				return VoiceGroup.Gunshot;
			case Category.Impact:
				return VoiceGroup.Impact;
			default:
				return VoiceGroup.Other;
		}
	}

	private static int GetVoiceGroupBudget(VoiceGroup _group)
	{
		switch (_group)
		{
			case VoiceGroup.Gunshot:
				return c_GunshotVoiceBudget;
			case VoiceGroup.Impact:
				return c_ImpactVoiceBudget;
			default:
				return c_OtherVoiceBudget;
		}
	}

	private static int ResolveUnityPriority(VoiceGroup _group)
	{
		switch (_group)
		{
			case VoiceGroup.Gunshot:
				return c_UnityPriorityGunshot;
			case VoiceGroup.Impact:
				return c_UnityPriorityImpact;
			default:
				return c_UnityPriorityOther;
		}
	}

	private static int ResolveUnityPriority(Category _category)
	{
		if (_category == Category.RocketLauncher)
			return c_UnityPriorityGunshot;

		return ResolveUnityPriority(ResolveVoiceGroup(_category));
	}

	private static bool TryAcquireVoiceSlot(
		VoiceGroup _group,
		int _priority,
		out int _slotIndex,
		bool _forceMaximumPriority = false)
	{
		_slotIndex = -1;
		EnsureVoicePool();
		ReleaseFinishedSlots();

		if (_forceMaximumPriority)
		{
			if (TryFindFreeVoiceSlot(out _slotIndex))
				return true;

			if (TryForceStealWeakestAnyGroup(out _slotIndex))
				return true;

			return TryForceStealWeakestInGroup(_group, out _slotIndex);
		}

		int playing = CountPlayingVoiceGroup(_group);
		int budget = GetVoiceGroupBudget(_group);

		if (playing < budget)
		{
			if (TryFindFreeVoiceSlot(out _slotIndex))
				return true;

			if (TryStealFromOverBudgetGroups(_group, out _slotIndex))
				return true;

			if (_group == VoiceGroup.Gunshot &&
			    (TryForceStealWeakestInGroup(VoiceGroup.Other, out _slotIndex) ||
			     TryForceStealWeakestInGroup(VoiceGroup.Impact, out _slotIndex)))
				return true;
		}

		if (playing >= budget)
		{
			if (TryFindStealCandidateInGroup(_priority, _group, out _slotIndex))
				return true;

			if (_group == VoiceGroup.Gunshot)
				return TryForceStealWeakestInGroup(VoiceGroup.Gunshot, out _slotIndex);

			return false;
		}

		if (_group == VoiceGroup.Gunshot)
			return TryForceStealWeakestVoiceForGunshot(out _slotIndex);

		return false;
	}

	private static void ReleaseFinishedSlots()
	{
		if (s_VoiceSlots == null)
			return;

		float now = Time.unscaledTime;
		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			AudioSource source = slot.Source;
			if (source == null)
				continue;

			if (source.isPlaying && now < slot.EndUnscaledTime)
				continue;

			if (source.isPlaying)
				source.Stop();

			source.clip = null;
			slot.Priority = 0;
			slot.EndUnscaledTime = 0f;
			slot.IsBulletWhiz = false;
		}
	}

	private static bool TryStealFromOverBudgetGroups(VoiceGroup _requestGroup, out int _slotIndex)
	{
		_slotIndex = -1;

		if (TryForceStealIfOverBudget(VoiceGroup.Other, out _slotIndex))
			return true;

		if (_requestGroup != VoiceGroup.Impact &&
		    TryForceStealIfOverBudget(VoiceGroup.Impact, out _slotIndex))
			return true;

		if (_requestGroup == VoiceGroup.Gunshot &&
		    TryForceStealIfOverBudget(VoiceGroup.Gunshot, out _slotIndex))
			return true;

		return false;
	}

	private static bool TryForceStealIfOverBudget(VoiceGroup _group, out int _slotIndex)
	{
		_slotIndex = -1;
		if (CountPlayingVoiceGroup(_group) <= GetVoiceGroupBudget(_group))
			return false;

		return TryForceStealWeakestInGroup(_group, out _slotIndex);
	}

	private static bool TryFindStealCandidateInGroup(
		int _newPriority,
		VoiceGroup _victimGroup,
		out int _slotIndex)
	{
		_slotIndex = -1;
		int bestPriority = int.MaxValue;
		float soonestEnd = float.MaxValue;

		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			AudioSource source = slot.Source;
			if (!IsSlotActivelyPlaying(ref slot))
				continue;

			if (slot.Group != _victimGroup)
				continue;

			bool better =
				slot.Priority < bestPriority ||
				(slot.Priority == bestPriority && slot.EndUnscaledTime < soonestEnd);

			if (!better)
				continue;

			bestPriority = slot.Priority;
			soonestEnd = slot.EndUnscaledTime;
			_slotIndex = i;
		}

		if (_slotIndex < 0)
			return false;

		if (_newPriority <= bestPriority + c_PriorityStealMargin)
			return false;

		StopSlot(_slotIndex);
		return true;
	}

	private static bool TryForceStealWeakestVoiceForGunshot(out int _slotIndex)
	{
		if (TryForceStealWeakestInGroup(VoiceGroup.Other, out _slotIndex))
			return true;

		if (TryForceStealWeakestInGroup(VoiceGroup.Impact, out _slotIndex))
			return true;

		return TryForceStealWeakestInGroup(VoiceGroup.Gunshot, out _slotIndex);
	}

	private static bool TryForceStealWeakestInGroup(VoiceGroup _group, out int _slotIndex)
	{
		_slotIndex = -1;
		int bestPriority = int.MaxValue;
		float soonestEnd = float.MaxValue;

		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			if (!IsSlotActivelyPlaying(ref slot))
				continue;

			if (slot.Group != _group)
				continue;

			bool better =
				slot.Priority < bestPriority ||
				(slot.Priority == bestPriority && slot.EndUnscaledTime < soonestEnd);

			if (!better)
				continue;

			bestPriority = slot.Priority;
			soonestEnd = slot.EndUnscaledTime;
			_slotIndex = i;
		}

		if (_slotIndex < 0)
			return false;

		StopSlot(_slotIndex);
		return true;
	}

	private static void StopSlot(int _slotIndex)
	{
		ref VoiceSlot slot = ref s_VoiceSlots[_slotIndex];
		AudioSource source = slot.Source;
		if (source != null)
		{
			source.Stop();
			source.clip = null;
		}

		slot.Priority = 0;
		slot.EndUnscaledTime = 0f;
		slot.IsBulletWhiz = false;
	}

	private static bool IsSlotActivelyPlaying(ref VoiceSlot _slot)
	{
		AudioSource source = _slot.Source;
		if (source == null)
			return false;

		if (!source.isPlaying)
			return false;

		return Time.unscaledTime < _slot.EndUnscaledTime || source.time > 0f;
	}

	private static bool TryFindFreeVoiceSlot(out int _slotIndex)
	{
		_slotIndex = -1;
		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			AudioSource source = slot.Source;
			if (source == null)
				continue;

			if (IsSlotActivelyPlaying(ref slot))
				continue;

			if (source.isPlaying)
				source.Stop();

			source.clip = null;
			_slotIndex = i;
			return true;
		}

		return false;
	}

	private static int CountPlayingVoiceGroup(VoiceGroup _group)
	{
		if (s_VoiceSlots == null)
			return 0;

		int count = 0;
		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			if (!IsSlotActivelyPlaying(ref slot))
				continue;

			if (slot.Group != _group)
				continue;

			count++;
		}

		return count;
	}

	private static int ComputePriority(
		Vector3 _position,
		Transform _ownerOrNull,
		Category _category,
		float _estimatedVolume,
		EntityId _ownerId,
		EntityId _weaponSignatureId)
	{
		int priority = ResolveCategoryTier(_category) * c_TierScoreMultiplier;
		float listenerDistance = GetListenerDistance(_position);
		priority += Mathf.RoundToInt(1000f - listenerDistance + _estimatedVolume * 100f);

		if (IsOwnerSelected(_ownerOrNull))
			priority += Mathf.RoundToInt(c_SelectedUnitPriorityBonus);

		if (_category == Category.Gunshot &&
		    _weaponSignatureId.IsValid() &&
		    CountRecentDuplicateWeaponOwners(_weaponSignatureId, _ownerId) == 0)
			priority += 35;

		return priority;
	}

	private static int ResolveCategoryTier(Category _category)
	{
		switch (_category)
		{
			case Category.RocketLauncher:
				return c_TierRocketLauncher;
			case Category.Gunshot:
				return c_TierGunshot;
			case Category.Impact:
				return c_TierImpact;
			case Category.CombatSecondary:
				return c_TierCombatSecondary;
			default:
				return c_TierNonFire;
		}
	}

	private static int ComputeRocketLauncherPriority(
		Vector3 _position,
		Transform _ownerOrNull,
		float _estimatedVolume,
		EntityId _ownerId)
	{
		int priority = c_TierRocketLauncher * c_TierScoreMultiplier + c_RocketLauncherPriorityBonus;
		float listenerDistance = GetListenerDistance(_position);
		priority += Mathf.RoundToInt(1200f - listenerDistance + _estimatedVolume * 200f);

		if (IsOwnerSelected(_ownerOrNull))
			priority += Mathf.RoundToInt(c_SelectedUnitPriorityBonus);

		if (_ownerId.IsValid())
			priority += 120;

		return priority;
	}

	private static bool TryForceStealWeakestAnyGroup(out int _slotIndex)
	{
		_slotIndex = -1;
		int bestPriority = int.MaxValue;
		float soonestEnd = float.MaxValue;

		for (int i = 0; i < s_VoiceSlots.Length; i++)
		{
			ref VoiceSlot slot = ref s_VoiceSlots[i];
			if (!IsSlotActivelyPlaying(ref slot))
				continue;

			bool better =
				slot.Priority < bestPriority ||
				(slot.Priority == bestPriority && slot.EndUnscaledTime < soonestEnd);

			if (!better)
				continue;

			bestPriority = slot.Priority;
			soonestEnd = slot.EndUnscaledTime;
			_slotIndex = i;
		}

		if (_slotIndex < 0)
			return false;

		StopSlot(_slotIndex);
		return true;
	}

	private static bool IsOwnerSelected(Transform _ownerOrNull)
	{
		if (_ownerOrNull == null)
			return false;

		if (_ownerOrNull.TryGetComponent(out RtsUnitMember member))
			return member.IsSelected;

		RtsUnitMember parentMember = _ownerOrNull.GetComponentInParent<RtsUnitMember>();
		return parentMember != null && parentMember.IsSelected;
	}

	private static float GetListenerDistance(Vector3 _position)
	{
		return Vector3.Distance(_position, GetListenerPosition());
	}

	private static Vector3 GetListenerPosition()
	{
		Camera mainCamera = Camera.main;
		if (mainCamera != null)
			return mainCamera.transform.position;

		AudioListener listener = Object.FindAnyObjectByType<AudioListener>();
		return listener != null ? listener.transform.position : Vector3.zero;
	}

	private static float EstimateVolumeAtListener(Vector3 _position, float _volume, float _maxDistance)
	{
		float distance = GetListenerDistance(_position);
		if (distance >= _maxDistance)
			return 0f;

		float normalizedDistance = Mathf.Clamp01(distance / _maxDistance);
		float attenuation = Mathf.Lerp(
			1f,
			c_RolloffMinAudibleVolume,
			Mathf.Pow(normalizedDistance, c_RolloffAttenuationPower));
		return _volume * attenuation;
	}

	private static void EnsureVoicePool()
	{
		if (s_VoiceSlots != null)
			return;

		GameObject rootGo = new GameObject(nameof(CombatAudioManager));
		Object.DontDestroyOnLoad(rootGo);
		s_PoolRoot = rootGo.transform;

		s_VoiceSlots = new VoiceSlot[c_VoicePoolSize];
		for (int i = 0; i < c_VoicePoolSize; i++)
		{
			GameObject voiceGo = new GameObject($"Voice_{i}");
			voiceGo.transform.SetParent(s_PoolRoot, false);
			AudioSource source = voiceGo.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = false;
			source.spatialBlend = 1f;
			source.dopplerLevel = 0f;
			source.priority = c_UnityPriorityOther;
			s_VoiceSlots[i] = new VoiceSlot { Source = source, Group = VoiceGroup.Other };
		}
	}

	private static void ConfigureNonSpatial(AudioSource _source)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 0f;
		_source.dopplerLevel = 0f;
	}

	private static void ConfigureSpatial(AudioSource _source, float _minDistance, float _maxDistance)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 1f;
		_source.minDistance = Mathf.Max(0.01f, _minDistance);
		_source.maxDistance = Mathf.Max(_source.minDistance + 0.01f, _maxDistance);
		_source.rolloffMode = AudioRolloffMode.Custom;
		_source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, GetRolloffCurve());
		_source.dopplerLevel = 0f;
	}

	private static AnimationCurve GetRolloffCurve()
	{
		if (s_RolloffCurve != null)
			return s_RolloffCurve;

		Keyframe[] keys = new Keyframe[c_RolloffCurveKeyCount];
		for (int i = 0; i < c_RolloffCurveKeyCount; i++)
		{
			float normalizedDistance = i / (float)(c_RolloffCurveKeyCount - 1);
			float volume = Mathf.Lerp(
				c_RolloffMinAudibleVolume,
				1f,
				Mathf.Pow(1f - normalizedDistance, c_RolloffAttenuationPower));
			keys[i] = new Keyframe(normalizedDistance, volume);
		}

		s_RolloffCurve = new AnimationCurve(keys);
		return s_RolloffCurve;
	}
	#endregion
}
