using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// V1.9.1/V1.9.2 physical Exposure staging for calibration A–H.
/// Spawns a real collider so LOS / hit-zones produce Exposure01. Does not write Exposure01.
/// </summary>
public sealed class DetectionCalibrationExposureStaging
{
	#region Constants
	private const string c_CoverName = "CalibrationExposureCover";
	private const float c_CoverWidthMeters = 3.2f;
	private const float c_CoverDepthMeters = 0.4f;
	private const float c_CoverGapMeters = 0.8f;
	private const float c_MinCoverHeight = 0.12f;
	private const float c_MaxCoverHeight = 2.35f;
	private const float c_FullExposure = 0.999f;
	private const int c_HeightSearchIterations = 14;
	private const int c_RaycastHitBuffer = 16;
	#endregion

	#region Private Fields
	private GameObject m_Cover;
	private Transform m_Observer;
	private Transform m_Target;
	private float m_CoverHeightMeters;
	private float m_MeasuredExposure01 = 1f;
	private bool m_YawMirrored;
	private string m_Note = "none";
	private readonly RaycastHit[] m_Hits = new RaycastHit[c_RaycastHitBuffer];
	private readonly List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> m_AimScratch =
		new List<UnitBodyHitZoneVisionUtility.VisionAimCandidate>(32);
	#endregion

	#region Public Properties
	public float CoverHeightMeters => m_CoverHeightMeters;
	public float MeasuredExposure01 => m_MeasuredExposure01;
	public bool YawMirrored => m_YawMirrored;
	public string Note => m_Note;
	public string CoverName => m_Cover != null && m_Cover.activeSelf ? m_Cover.name : "none";
	public bool HasCover => m_Cover != null && m_Cover.activeSelf;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_Observer = null;
		m_Target = null;
		m_CoverHeightMeters = 0f;
		m_MeasuredExposure01 = 1f;
		m_YawMirrored = false;
		m_Note = "none";
		SetCoverActive(false);
		if (m_Cover == null)
			return;

		Object.Destroy(m_Cover);
		m_Cover = null;
	}

	public void Follow()
	{
		if (m_Cover == null || !m_Cover.activeSelf || m_Observer == null || m_Target == null)
			return;
		PlaceCoverTransform(m_Cover.transform, m_Observer, m_Target, m_CoverHeightMeters);
	}

	/// <summary>
	/// Place a waist-to-head wall so weighted hit-zone samples match <paramref name="_desiredExposure01"/>.
	/// </summary>
	public void Apply(Transform _observer, Transform _target, float _desiredExposure01)
	{
		m_Observer = _observer;
		m_Target = _target;
		bool yawMirrored = m_YawMirrored;
		string yawNote = yawMirrored ? m_Note : null;

		if (_observer == null || _target == null)
		{
			Clear();
			return;
		}

		m_YawMirrored = yawMirrored;
		float desired = Mathf.Clamp01(_desiredExposure01);
		SetCoverActive(false);
		Physics.SyncTransforms();
		float openExposure = MeasureExposure(_observer, _target);

		if (desired >= c_FullExposure)
		{
			m_CoverHeightMeters = 0f;
			m_MeasuredExposure01 = openExposure;
			m_Note = ComposeNote(yawNote, "open-field (design Exposure=1)");
			return;
		}

		if (Mathf.Abs(openExposure - desired) <= 0.20f && openExposure > 0.001f)
		{
			m_CoverHeightMeters = 0f;
			m_MeasuredExposure01 = openExposure;
			m_Note = ComposeNote(yawNote, $"scene already in band openE={openExposure:0.00}");
			return;
		}

		EnsureCover();
		SetCoverActive(true);
		IgnoreUnitCollisions(_observer);
		IgnoreUnitCollisions(_target);
		Follow();
		Physics.SyncTransforms();

		float bestHeight = c_MinCoverHeight;
		float bestError = float.MaxValue;
		float bestExposure = openExposure;
		float lo = c_MinCoverHeight;
		float hi = c_MaxCoverHeight;

		for (int i = 0; i < c_HeightSearchIterations; i++)
		{
			float mid = 0.5f * (lo + hi);
			SetCoverHeight(mid);
			float exposure = MeasureExposure(_observer, _target);
			float error = Mathf.Abs(exposure - desired);
			bool keepVisible = desired <= 0.001f || exposure > 0.001f;
			if (keepVisible && error < bestError)
			{
				bestError = error;
				bestHeight = mid;
				bestExposure = exposure;
			}

			if (exposure > desired)
				lo = mid;
			else
				hi = mid;
		}

		if (desired > 0.001f && bestExposure <= 0.001f)
		{
			for (float height = c_MaxCoverHeight; height >= c_MinCoverHeight; height -= 0.05f)
			{
				SetCoverHeight(height);
				float exposure = MeasureExposure(_observer, _target);
				if (exposure <= 0.001f)
					continue;
				bestHeight = height;
				bestExposure = exposure;
				break;
			}
		}

		SetCoverHeight(bestHeight);
		m_MeasuredExposure01 = MeasureExposure(_observer, _target);
		m_YawMirrored = yawMirrored;
		m_Note = ComposeNote(
			yawNote,
			$"cover h={m_CoverHeightMeters:0.00}m designE={desired:0.00} stagedE={m_MeasuredExposure01:0.00}");
	}

	public bool IsSceneFullyBlocking(Transform _observer, Transform _target)
	{
		if (_observer == null || _target == null)
			return false;

		Vector3 eye = GetEye(_observer);
		Vector3[] probes =
		{
			_target.position + Vector3.up * 1.55f,
			_target.position + Vector3.up * 1.15f,
			_target.position + Vector3.up * 0.70f
		};

		int blocked = 0;
		for (int i = 0; i < probes.Length; i++)
		{
			if (!HasLosToPoint(eye, probes[i], _observer, _target, out _))
				blocked++;
		}

		return blocked == probes.Length;
	}

	public void HideCover()
	{
		SetCoverActive(false);
	}

	public void BeginScenario()
	{
		m_YawMirrored = false;
		m_Note = "none";
	}

	public void MarkYawMirrored(bool _mirrored, string _note)
	{
		m_YawMirrored = _mirrored;
		m_Note = string.IsNullOrEmpty(_note) ? "none" : _note;
	}
	#endregion

	#region Private Methods
	private static string ComposeNote(string _prefix, string _body)
	{
		if (string.IsNullOrEmpty(_prefix) || _prefix == "none")
			return _body;
		return _prefix + "; " + _body;
	}

	private void SetCoverActive(bool _active)
	{
		if (m_Cover != null && m_Cover.activeSelf != _active)
			m_Cover.SetActive(_active);
	}

	private void EnsureCover()
	{
		if (m_Cover != null)
			return;

		m_Cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
		m_Cover.name = c_CoverName;
		m_Cover.hideFlags = HideFlags.DontSave;
		m_Cover.layer = 0;
		m_Cover.transform.position = new Vector3(0f, -1000f, 0f);
		if (m_Cover.TryGetComponent(out Rigidbody body))
			Object.Destroy(body);
		if (m_Cover.TryGetComponent(out MeshRenderer renderer))
		{
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}
	}

	private void SetCoverHeight(float _heightMeters)
	{
		m_CoverHeightMeters = Mathf.Clamp(_heightMeters, c_MinCoverHeight, c_MaxCoverHeight);
		Follow();
		Physics.SyncTransforms();
	}

	private static void PlaceCoverTransform(
		Transform _cover,
		Transform _observer,
		Transform _target,
		float _heightMeters)
	{
		Vector3 toTarget = _target.position - _observer.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 0.0001f)
			toTarget = _observer.forward;
		toTarget.Normalize();

		float height = Mathf.Max(c_MinCoverHeight, _heightMeters);
		Vector3 pos = _target.position - toTarget * c_CoverGapMeters;
		pos.y = _target.position.y + height * 0.5f;
		_cover.SetPositionAndRotation(pos, Quaternion.LookRotation(toTarget, Vector3.up));
		_cover.localScale = new Vector3(c_CoverWidthMeters, height, c_CoverDepthMeters);
	}

	private void IgnoreUnitCollisions(Transform _root)
	{
		if (m_Cover == null || _root == null || !m_Cover.TryGetComponent(out Collider coverCol))
			return;

		Collider[] unitCols = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < unitCols.Length; i++)
		{
			if (unitCols[i] == null)
				continue;
			Physics.IgnoreCollision(coverCol, unitCols[i], true);
		}
	}

	private float MeasureExposure(Transform _observer, Transform _target)
	{
		UnitBodyHitZone[] zones = _target.GetComponentsInChildren<UnitBodyHitZone>(true);
		if (zones == null || zones.Length == 0)
			return 0f;

		Vector3 eye = GetEye(_observer);
		float totalWeight = 0f;
		float visibleWeight = 0f;

		for (int z = 0; z < zones.Length; z++)
		{
			UnitBodyHitZone zone = zones[z];
			if (!UnitBodyHitZoneVisionUtility.IsUsableVisionZone(zone, out Collider zoneCol))
				continue;

			UnitBodyHitZoneVisionUtility.BuildAimCandidates(zone.BodyPart, zoneCol, m_AimScratch);
			for (int i = 0; i < m_AimScratch.Count; i++)
			{
				UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimScratch[i];
				float weight = Mathf.Max(0.0001f, candidate.Weight);
				totalWeight += weight;
				if (HasLosToPoint(eye, candidate.Point, _observer, _target, out bool hitTarget) && hitTarget)
					visibleWeight += weight;
			}
		}

		return totalWeight > 0.0001f ? Mathf.Clamp01(visibleWeight / totalWeight) : 0f;
	}

	private bool HasLosToPoint(
		Vector3 _eye,
		Vector3 _worldPoint,
		Transform _observerRoot,
		Transform _targetRoot,
		out bool _hitTarget)
	{
		_hitTarget = false;
		Vector3 dir = _worldPoint - _eye;
		float dist = dir.magnitude;
		if (dist < 0.02f)
		{
			_hitTarget = true;
			return true;
		}

		dir /= dist;
		Vector3 origin = _eye + dir * 0.08f;
		int hitCount = Physics.RaycastNonAlloc(
			origin,
			dir,
			m_Hits,
			dist - 0.08f,
			~0,
			QueryTriggerInteraction.Collide);
		if (hitCount <= 0)
			return false;

		for (int i = 1; i < hitCount; i++)
		{
			RaycastHit key = m_Hits[i];
			int j = i - 1;
			while (j >= 0 && m_Hits[j].distance > key.distance)
			{
				m_Hits[j + 1] = m_Hits[j];
				j--;
			}

			m_Hits[j + 1] = key;
		}

		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_Hits[h];
			Collider hc = hit.collider;
			if (hc == null)
				continue;
			if (hc.transform.IsChildOf(_observerRoot))
				continue;
			if (hc.isTrigger && !hc.transform.IsChildOf(_targetRoot))
				continue;

			if (hc.transform.IsChildOf(_targetRoot) || hc.transform == _targetRoot)
			{
				_hitTarget = true;
				return true;
			}

			return false;
		}

		return false;
	}

	private static Vector3 GetEye(Transform _observer)
	{
		if (_observer != null && _observer.TryGetComponent(out UnitObservationSource source))
			return source.GetEyeWorldPosition();
		return _observer != null ? _observer.position + Vector3.up * 1.6f : Vector3.up * 1.6f;
	}
	#endregion
}
