using UnityEngine;

namespace CombatVehicleSystem
{
	public class WeaponMount : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private Transform m_Muzzle;
		[SerializeField] private Transform m_RecoilAnchor;
		[SerializeField] private Transform m_BarrelRecoilTransform;
		[SerializeField] private GameObject m_ShellPrefab;
		[SerializeField] private GameObject m_HitPrefab;
		[SerializeField] private ParticleSystem m_MuzzleFlash;
		[SerializeField] private AudioSource m_ShotAudio;
		[SerializeField] private AudioClip m_ShotClip;
		#endregion

		#region Private Fields
		private Rigidbody m_Body;
		private bool m_Active;
		private float m_FireInterval = 0.17f;
		private float m_ShellSpeed = 200f;
		private float m_HullRecoilForce = 100f;
		private int m_MagazineSize = 300;
		private bool m_InfiniteAmmo;
		private Vector3 m_ShotSpread = new Vector3(0.1f, 0.1f, 0.1f);
		private Vector3 m_BarrelKick;
		private float m_BarrelKickSpeed = 8f;
		private float m_BarrelReturnSpeed = 18f;
		private float m_HitFxLifetime = 10f;
		private float m_ShellLifetime = 25f;
		private float m_MinShotPitch = 0.9f;
		private float m_MaxShotPitch = 1.1f;
		private float m_FireTimer;
		private int m_RoundsInMagazine;
		private Vector3 m_RecoilOffset;
		#endregion

		#region Public Properties
		public int RoundsInMagazine => m_RoundsInMagazine;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Body = GetComponent<Rigidbody>();
			m_RoundsInMagazine = m_MagazineSize;
			m_FireTimer = m_FireInterval;
		}

		private void FixedUpdate()
		{
			if (m_BarrelRecoilTransform == null)
				return;

			m_RecoilOffset = Vector3.Slerp(m_RecoilOffset, Vector3.zero, m_BarrelReturnSpeed * Time.deltaTime);
			m_BarrelRecoilTransform.localPosition = Vector3.Slerp(
				m_BarrelRecoilTransform.localPosition,
				-m_RecoilOffset,
				m_BarrelKickSpeed * Time.fixedDeltaTime);
		}
		#endregion

		#region Public Methods
		public void ApplyTuning(VehicleTuning _tuning)
		{
			if (_tuning == null)
				return;

			m_FireInterval = _tuning.FireInterval;
			m_ShellSpeed = _tuning.ShellSpeed;
			m_HullRecoilForce = _tuning.HullRecoilForce;
			m_MagazineSize = _tuning.MagazineSize;
			m_InfiniteAmmo = _tuning.InfiniteAmmo;
			m_ShotSpread = _tuning.ShotSpread;
			m_BarrelKick = _tuning.BarrelKick;
			m_BarrelKickSpeed = _tuning.BarrelKickSpeed;
			m_BarrelReturnSpeed = _tuning.BarrelReturnSpeed;
			m_HitFxLifetime = _tuning.HitFxLifetime;
			m_ShellLifetime = _tuning.ShellLifetime;
			m_MinShotPitch = _tuning.MinShotPitch;
			m_MaxShotPitch = _tuning.MaxShotPitch;

			if (m_RoundsInMagazine <= 0 || m_RoundsInMagazine > m_MagazineSize)
				m_RoundsInMagazine = m_MagazineSize;
		}

		public void Configure(
			Transform _muzzle,
			Transform _recoilAnchor,
			Transform _barrelRecoil,
			GameObject _shellPrefab,
			GameObject _hitPrefab,
			ParticleSystem _muzzleFlash,
			AudioSource _shotAudio,
			AudioClip _shotClip)
		{
			m_Muzzle = _muzzle;
			m_RecoilAnchor = _recoilAnchor;
			m_BarrelRecoilTransform = _barrelRecoil;
			m_ShellPrefab = _shellPrefab;
			m_HitPrefab = _hitPrefab;
			m_MuzzleFlash = _muzzleFlash;
			m_ShotAudio = _shotAudio;
			m_ShotClip = _shotClip;
		}

		public void SetActive(bool _active)
		{
			m_Active = _active;
		}

		public void ReloadMagazine()
		{
			m_RoundsInMagazine = m_MagazineSize;
		}

		public void TickFire(VehicleCommand _command)
		{
			if (m_FireTimer < m_FireInterval)
				m_FireTimer += Time.deltaTime;

			if (!m_Active || !_command.FireHeld)
				return;

			TryFire();
		}
		#endregion

		#region Private Methods
		private void TryFire()
		{
			if (m_FireTimer < m_FireInterval)
				return;
			if (!m_InfiniteAmmo && m_RoundsInMagazine <= 0)
				return;
			if (m_Muzzle == null || m_ShellPrefab == null)
				return;

			if (!m_InfiniteAmmo)
				m_RoundsInMagazine--;

			m_RecoilOffset += m_BarrelKick;

			Vector3 spread = new Vector3(
				Random.Range(-m_ShotSpread.x, m_ShotSpread.x),
				Random.Range(-m_ShotSpread.y, m_ShotSpread.y),
				Random.Range(-m_ShotSpread.z, m_ShotSpread.z));
			Quaternion shotRotation = m_Muzzle.rotation * Quaternion.Euler(spread);

			GameObject shell = Instantiate(m_ShellPrefab, m_Muzzle.position, shotRotation);
			if (shell.TryGetComponent(out ShellProjectile projectile))
				projectile.Configure(m_HitPrefab, m_HitFxLifetime, m_ShellLifetime);

			if (shell.TryGetComponent(out Rigidbody shellBody))
				shellBody.AddForce(shotRotation * Vector3.forward * m_ShellSpeed, ForceMode.Impulse);

			if (m_MuzzleFlash != null)
				m_MuzzleFlash.Play();

			if (m_ShotAudio != null && m_ShotClip != null)
			{
				m_ShotAudio.pitch = Random.Range(m_MinShotPitch, m_MaxShotPitch);
				m_ShotAudio.PlayOneShot(m_ShotClip);
			}

			if (m_Body != null && m_RecoilAnchor != null)
				m_Body.AddForceAtPosition(m_RecoilAnchor.forward * m_HullRecoilForce, m_RecoilAnchor.position, ForceMode.Impulse);

			m_FireTimer = 0f;
		}
		#endregion
	}
}
