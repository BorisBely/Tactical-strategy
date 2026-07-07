using UnityEngine;

/// <summary>
/// Отказы и клины: двухканальная вероятность (износ / загрязнение, взаимоисключающе),
/// единый сценарий снятия (фаза A: rack с магазином; фаза B: снять магазин → rack → вставить).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(52)]
public sealed class UnitWeaponMalfunctionController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponShellEjection m_ShellEjection;
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private UnitEquipment m_Equipment;

	[Header("Клин за выстрел")]
	[Tooltip("Ступени C/F — пороги в коде. Формула: нагрузка по износу/грязи × влияние на оружии × надёжность × патрон/магазин/модули; сначала канал износа, иначе загрязнения. В смешанной ступени — доля лёгкого отказа.")]
	[SerializeField, Range(0f, 1f)] private float m_LightShareInMixedTier = 0.5f;

	[Header("Передёргивание при клине (аниматор)")]
	[Tooltip("После конца клипа затвора IsCyclingBolt сбрасывается в false; перед следующим rack ждём столько секунд, чтобы граф успел выйти в исходное состояние и снова принять переход по true. 0 = только конец кадра.")]
	[SerializeField, Min(0f)] private float m_MalfunctionBoltRearmDelaySeconds = 0.08f;

	[Header("Отладка")]
	[SerializeField] private string m_DebugLastJamRoll;
	#endregion

	#region Private Fields
	private bool m_PendingMalfunctionBoltApplyRearmDelay;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_ShellEjection == null)
			m_ShellEjection = GetComponent<UnitWeaponShellEjection>();
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponentInChildren<CharacterInventory>(true);
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);

		if (m_WeaponRuntime != null)
			m_WeaponRuntime.RegisterMalfunctionController(this);
	}

	private void OnDestroy()
	{
		if (m_WeaponRuntime != null)
			m_WeaponRuntime.UnregisterMalfunctionController(this);
	}
	#endregion

	#region Public API — fire pipeline
	/// <summary>
	/// Вызывается из <see cref="UnitWeaponRuntime.TryConsumeShot"/> до расхода патронника.
	/// </summary>
	public bool EvaluateBeforeChamberedShot(float _time, out WeaponShotAttemptResult _result)
	{
		_result = WeaponShotAttemptResult.NoWeapon;

		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		WeaponRuntimeState rs = m_WeaponRuntime.RuntimeState;

		if (rs.IsTerminallyBroken)
		{
			_result = WeaponShotAttemptResult.WeaponBroken;
			return true;
		}

		if (Transient.HasActiveMalfunction)
		{
			bool recovering =
				Transient.MalfunctionBoltAnimInProgress ||
				(m_ReloadController != null &&
				 (m_ReloadController.IsCyclingBolt ||
				  m_ReloadController.IsReloadingWeapon));

			if (recovering)
			{
				_result = WeaponShotAttemptResult.Busy;
				return true;
			}

			_result = WeaponShotAttemptResult.MalfunctionStoppage;
			return true;
		}

		if (!rs.HasRoundInChamber)
			return false;

		int integrityC = GetIntegrityPercent(rs.Wear01);
		int foulingF = GetFoulingPercent(rs.Fouling01);

		if (integrityC <= 0 || foulingF >= 100)
		{
			ApplyTerminalFailure();
			_result = WeaponShotAttemptResult.WeaponBroken;
			return true;
		}

		if (!TryRollNewMalfunction(rs, out WeaponMalfunctionKind kind, out WeaponMalfunctionChannel channel))
			return false;

		EnterMalfunction(kind, channel, WeaponMalfunctionPhase.PhaseARackWithMag);
		_result = WeaponShotAttemptResult.MalfunctionOccurred;
		return true;
	}

	/// <summary>
	/// Вызывается из <see cref="UnitWeaponReloadController.AnimationEvent_FinishWeaponReload"/> до стандартной логики.
	/// </summary>
	public bool TryConsumeBoltFinishEvent()
	{
		if (m_WeaponRuntime == null || !Transient.HasActiveMalfunction)
			return false;

		WeaponRuntimeState rs = m_WeaponRuntime.RuntimeState;
		if (rs == null)
			return false;

		bool stripPhaseB = Transient.MalfunctionPhase == WeaponMalfunctionPhase.PhaseBStripAndReinsert &&
		                   m_ReloadController != null &&
		                   m_ReloadController.IsMalfunctionStripReinsertReloadActive &&
		                   !m_ReloadController.MagazineInsertCompletedThisReload;
		bool phaseBCycling = stripPhaseB && m_ReloadController.IsCyclingBolt;
		bool phaseAMalfunctionBolt = Transient.MalfunctionPhase == WeaponMalfunctionPhase.PhaseARackWithMag &&
		                             Transient.MalfunctionBoltAnimInProgress;

		if (!phaseAMalfunctionBolt && !phaseBCycling)
			return false;

		WeaponMalfunctionPhase phase = Transient.MalfunctionPhase;
		int attempt = Transient.MalfunctionRackAttemptIndex;
		float clearChance = GetRackClearChanceForAttempt(attempt);
		bool rngSuccess = Random.value < clearChance;

		bool isHeavy = Transient.MalfunctionKind == WeaponMalfunctionKind.Heavy;
		bool phaseA = phase == WeaponMalfunctionPhase.PhaseARackWithMag;

		if (phaseA && isHeavy)
		{
			Transient.SetMalfunctionBoltAnimInProgress(false);
			if (attempt >= 2)
				BeginHeavyPhaseBStripReload();
			else
				Transient.SetMalfunctionRackAttemptIndex(attempt + 1);

			if (attempt < 2)
			{
				// Сброс bool затвора и пауза перед повторным true — иначе переходы «только из false» не срабатывают.
				NotifyBoltCycleEndedForNextMalfunctionRack();
				TryQueueNextRackNextFrame(applyRearmDelayAfterBoltEnd: true);
			}

			return true;
		}

		if (phaseA && Transient.MalfunctionKind == WeaponMalfunctionKind.Light)
		{
			if (!rngSuccess)
			{
				Transient.SetMalfunctionBoltAnimInProgress(false);
				if (attempt >= 2)
				{
					Debug.LogWarning($"{nameof(UnitWeaponMalfunctionController)}: лёгкий отказ — принудительное извлечение после 3-й попытки.", this);
					ResolveSuccessfulRackClear(rs, true);
				}
				else
				{
					Transient.SetMalfunctionRackAttemptIndex(attempt + 1);
					NotifyBoltCycleEndedForNextMalfunctionRack();
					TryQueueNextRackNextFrame(applyRearmDelayAfterBoltEnd: true);
				}

				return true;
			}

			ResolveSuccessfulRackClear(rs, true);
			return true;
		}

		if (phase == WeaponMalfunctionPhase.PhaseBStripAndReinsert &&
		    m_ReloadController != null &&
		    m_ReloadController.IsMalfunctionStripReinsertReloadActive &&
		    !m_ReloadController.MagazineInsertCompletedThisReload)
		{
			if (!rngSuccess)
			{
				Transient.SetMalfunctionBoltAnimInProgress(false);
				if (attempt >= 2)
					ResolveSuccessfulRackClear(rs, false);
				else
				{
					Transient.SetMalfunctionRackAttemptIndex(attempt + 1);
					NotifyBoltCycleEndedForNextMalfunctionRack();
					TryQueueNextRackNextFrame(applyRearmDelayAfterBoltEnd: true);
				}

				return true;
			}

			ResolveSuccessfulRackClear(rs, false);
			return true;
		}

		return false;
	}

	public void OnMalfunctionStripReloadInsertComplete()
	{
		if (!Transient.HasActiveMalfunction)
			return;
		if (Transient.MalfunctionPhase != WeaponMalfunctionPhase.PhaseBStripAndReinsert)
			return;

		Transient.ClearMalfunction();
		m_DebugLastJamRoll = "Heavy cleared after strip reload insert";
	}

	public bool IsMalfunctionBoltRecoveryContext => Transient.HasActiveMalfunction;

	/// <summary>Вызывается из <see cref="UnitWeaponReloadController.TryStartMalfunctionBoltRack"/> до смены параметров аниматора.</summary>
	public void NotifyBoltAnimStarting()
	{
		Transient?.SetMalfunctionBoltAnimInProgress(true);
	}

	/// <summary>ИИ/ввод: следующий rack при ожидании снятия отказа (когда нет активной анимации затвора/перезарядки).</summary>
	public bool TryRequestManualRackForMalfunction()
	{
		if (m_WeaponRuntime == null || !Transient.HasActiveMalfunction || Transient.MalfunctionBoltAnimInProgress)
			return false;
		if (m_ReloadController == null)
			return false;
		if (m_ReloadController.IsCyclingBolt)
			return false;
		if (m_ReloadController.IsReloadingWeapon && !m_ReloadController.IsMalfunctionStripReinsertReloadActive)
			return false;

		TryQueueNextRackNextFrame();
		return true;
	}
	#endregion

	#region Private Properties
	private EquippedWeaponTransientState Transient => m_WeaponRuntime != null ? m_WeaponRuntime.TransientState : null;
	#endregion

	#region Private Methods
	private void EnterMalfunction(WeaponMalfunctionKind _kind, WeaponMalfunctionChannel _channel, WeaponMalfunctionPhase _phase)
	{
		Transient.SetMalfunction(_kind, _channel, _phase);
		m_FireController?.StopFiring();
		m_DebugLastJamRoll = $"Jam {_kind} ({_channel})";
		PlayMalfunctionEntrySound();
		TryQueueNextRackNextFrame();
	}

	private void BeginHeavyPhaseBStripReload()
	{
		m_ReloadController?.NotifyMalfunctionBoltHandledEnd();
		Transient.SetMalfunctionPhase(WeaponMalfunctionPhase.PhaseBStripAndReinsert);
		if (m_ReloadController == null || !m_ReloadController.TryStartMalfunctionStripReinsertReload())
		{
			Debug.LogWarning($"{nameof(UnitWeaponMalfunctionController)}: не удалось начать перезарядку снятия для тяжёлого клина.", this);
			Transient.ClearMalfunction();
		}
	}

	/// <param name="_clearLightMalfunction">Если true и лёгкий клин в фазе A — снять отказ полностью.</param>
	/// <summary>Сброс IsCyclingBolt в аниматоре перед следующим rack; иначе <see cref="UnitWeaponReloadController.TryStartMalfunctionBoltRack"/> не проходит и переходы ждут false.</summary>
	private void NotifyBoltCycleEndedForNextMalfunctionRack()
	{
		m_ReloadController?.NotifyMalfunctionBoltHandledEnd();
	}

	private void ResolveSuccessfulRackClear(WeaponRuntimeState _rs, bool _clearLightMalfunction)
	{
		m_ReloadController?.TryPlayBoltCycleSoundPublic();

		if (_rs.HasRoundInChamber && _rs.TryConsumeRound(out AmmoDefinition extracted))
			m_ShellEjection?.SpawnShellForAmmo(extracted);

		if (_clearLightMalfunction &&
		    Transient.MalfunctionKind == WeaponMalfunctionKind.Light &&
		    Transient.MalfunctionPhase == WeaponMalfunctionPhase.PhaseARackWithMag)
		{
			Transient.ClearMalfunction();
			m_DebugLastJamRoll = "Light jam cleared";
		}

		Transient.SetMalfunctionBoltAnimInProgress(false);
		m_ReloadController?.NotifyMalfunctionBoltHandledEnd();
	}

	private void TryQueueNextRackNextFrame(bool applyRearmDelayAfterBoltEnd = false)
	{
		if (!Transient.HasActiveMalfunction)
			return;
		m_PendingMalfunctionBoltApplyRearmDelay = applyRearmDelayAfterBoltEnd;
		StopCoroutine(nameof(CoStartBoltAfterFrame));
		StartCoroutine(CoStartBoltAfterFrame());
	}

	private System.Collections.IEnumerator CoStartBoltAfterFrame()
	{
		yield return null;
		if (!Transient.HasActiveMalfunction)
			yield break;

		if (m_PendingMalfunctionBoltApplyRearmDelay)
		{
			yield return new WaitForEndOfFrame();
			if (!Transient.HasActiveMalfunction)
				yield break;

			if (m_MalfunctionBoltRearmDelaySeconds > 0f)
				yield return new WaitForSeconds(m_MalfunctionBoltRearmDelaySeconds);

			m_PendingMalfunctionBoltApplyRearmDelay = false;
		}

		if (!Transient.HasActiveMalfunction)
			yield break;

		if (Transient.MalfunctionPhase == WeaponMalfunctionPhase.PhaseBStripAndReinsert &&
		    m_ReloadController != null &&
		    m_ReloadController.IsMalfunctionStripReinsertReloadActive)
		{
			m_ReloadController.RestartMalfunctionBoltCycleDuringStripReload();
			yield break;
		}

		m_ReloadController?.TryStartMalfunctionBoltRack();
	}

	private static float GetRackClearChanceForAttempt(int _attemptIndex)
	{
		return _attemptIndex switch
		{
			0 => 0.5f,
			1 => 0.75f,
			_ => 1f
		};
	}

	private bool TryRollNewMalfunction(WeaponRuntimeState _rs, out WeaponMalfunctionKind _kind, out WeaponMalfunctionChannel _channel)
	{
		_kind = WeaponMalfunctionKind.None;
		_channel = WeaponMalfunctionChannel.None;

		float pWear = ComputeWearJamProbability(_rs, out WeaponMalfunctionTier wearTier);
		if (pWear > 0f && Random.value < pWear && TryPickKindFromTier(wearTier, out _kind))
		{
			_channel = WeaponMalfunctionChannel.Wear;
			return true;
		}

		float pFoul = ComputeFoulingJamProbability(_rs, out WeaponMalfunctionTier foulTier);
		if (pFoul > 0f && Random.value < pFoul && TryPickKindFromTier(foulTier, out _kind))
		{
			_channel = WeaponMalfunctionChannel.Fouling;
			return true;
		}

		return false;
	}

	private float ComputeWearJamProbability(WeaponRuntimeState _rs, out WeaponMalfunctionTier _tier)
	{
		_tier = WeaponMalfunctionTier.None;
		WeaponDefinition wd = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (wd == null)
			return 0f;

		int integrityC = GetIntegrityPercent(_rs.Wear01);
		_tier = GetWearTierFromIntegrity(integrityC);
		if (_tier == WeaponMalfunctionTier.None || _tier == WeaponMalfunctionTier.Terminal)
			return 0f;

		float t0 = Mathf.Clamp01(wd.WearJamStartThreshold);
		float stress = Mathf.InverseLerp(t0, 1f, _rs.Wear01);
		if (stress <= 0f)
			return 0f;

		float tierM = GetTierJamStressMultiplier(_tier);
		float jamProduct = _rs.GetJamRiskProductForShot(_rs.ChamberedAmmoDefinition);
		float p = stress * wd.WearJamInfluence * tierM * GetReliabilityJamFactor(wd) * jamProduct;
		return Mathf.Clamp01(p);
	}

	private float ComputeFoulingJamProbability(WeaponRuntimeState _rs, out WeaponMalfunctionTier _tier)
	{
		_tier = WeaponMalfunctionTier.None;
		WeaponDefinition wd = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (wd == null)
			return 0f;

		int foulingF = GetFoulingPercent(_rs.Fouling01);
		_tier = GetFoulingTierFromFouling(foulingF);
		if (_tier == WeaponMalfunctionTier.None || _tier == WeaponMalfunctionTier.Terminal)
			return 0f;

		float f0 = Mathf.Clamp01(wd.FoulingJamStartThreshold);
		float stress = Mathf.InverseLerp(f0, 1f, _rs.Fouling01);
		if (stress <= 0f)
			return 0f;

		float tierM = GetTierJamStressMultiplier(_tier);
		float jamProduct = _rs.GetJamRiskProductForShot(_rs.ChamberedAmmoDefinition);
		float p = stress * wd.FoulingJamInfluence * tierM * GetReliabilityJamFactor(wd) * jamProduct;
		return Mathf.Clamp01(p);
	}

	private static float GetReliabilityJamFactor(WeaponDefinition _weaponDefinition)
	{
		if (_weaponDefinition == null)
			return 1f;

		float rel = Mathf.Clamp01(_weaponDefinition.Reliability);
		return Mathf.Lerp(1.35f, 0.25f, rel);
	}

	private static float GetTierJamStressMultiplier(WeaponMalfunctionTier _tier)
	{
		return _tier switch
		{
			WeaponMalfunctionTier.LightOnly => 0.35f,
			WeaponMalfunctionTier.LightOrHeavy => 0.7f,
			WeaponMalfunctionTier.HeavyOnly => 1f,
			_ => 0f
		};
	}

	private bool TryPickKindFromTier(WeaponMalfunctionTier _tier, out WeaponMalfunctionKind _kind)
	{
		switch (_tier)
		{
			case WeaponMalfunctionTier.LightOnly:
				_kind = WeaponMalfunctionKind.Light;
				return true;
			case WeaponMalfunctionTier.HeavyOnly:
				_kind = WeaponMalfunctionKind.Heavy;
				return true;
			case WeaponMalfunctionTier.LightOrHeavy:
				_kind = Random.value < m_LightShareInMixedTier ? WeaponMalfunctionKind.Light : WeaponMalfunctionKind.Heavy;
				return true;
			default:
				_kind = WeaponMalfunctionKind.None;
				return false;
		}
	}

	public static int GetIntegrityPercent(float _wear01)
	{
		return Mathf.Clamp(Mathf.RoundToInt(100f * (1f - Mathf.Clamp01(_wear01))), 0, 100);
	}

	public static int GetFoulingPercent(float _fouling01)
	{
		return Mathf.Clamp(Mathf.RoundToInt(100f * Mathf.Clamp01(_fouling01)), 0, 100);
	}

	public static WeaponMalfunctionTier GetWearTierFromIntegrity(int _C)
	{
		if (_C <= 0)
			return WeaponMalfunctionTier.Terminal;
		if (_C >= 80)
			return WeaponMalfunctionTier.None;
		if (_C >= 60)
			return WeaponMalfunctionTier.LightOnly;
		if (_C >= 40)
			return WeaponMalfunctionTier.LightOrHeavy;
		return WeaponMalfunctionTier.HeavyOnly;
	}

	public static WeaponMalfunctionTier GetFoulingTierFromFouling(int _F)
	{
		if (_F >= 100)
			return WeaponMalfunctionTier.Terminal;
		if (_F <= 20)
			return WeaponMalfunctionTier.None;
		if (_F <= 40)
			return WeaponMalfunctionTier.LightOnly;
		if (_F <= 60)
			return WeaponMalfunctionTier.LightOrHeavy;
		return WeaponMalfunctionTier.HeavyOnly;
	}

	private void ApplyTerminalFailure()
	{
		if (m_WeaponRuntime?.RuntimeState != null)
			m_WeaponRuntime.RuntimeState.SetTerminallyBroken(true);

		if (m_CharacterInventory != null && m_CharacterInventory.HasMainHandEquipment)
			m_CharacterInventory.TryUnequipMainHandToBag();

		Debug.LogWarning($"{nameof(UnitWeaponMalfunctionController)}: оружие негодно (терминальное состояние).", this);
	}

	private void PlayMalfunctionEntrySound()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null || !weaponDefinition.TryPickMalfunctionClickSound(out AudioClip clip))
			return;

		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			GetBarrelOrUnitWorldPosition(),
			weaponDefinition.MalfunctionClickSoundVolume,
			40f);
	}

	private Vector3 GetBarrelOrUnitWorldPosition()
	{
		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.BarrelTransform != null)
			return equipped.BarrelTransform.position;

		return transform.position;
	}
	#endregion
}
