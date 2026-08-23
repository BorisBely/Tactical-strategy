using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 16: one Publish, distance² fan-out to registered <see cref="DetectionProcessor"/>.
/// No FindObjects, no per-frame scan, no raycast.
/// </summary>
public static class WorldSoundHub
{
	#region Private Fields
	private static readonly List<DetectionProcessor> s_Listeners = new List<DetectionProcessor>(64);
	#endregion

	#region Public Properties
	public static int ListenerCount => s_Listeners.Count;
	public static int LastPublishDeliveryCount { get; private set; }
	#endregion

	#region Public Methods
	public static void Register(DetectionProcessor _processor)
	{
		if (_processor == null || s_Listeners.Contains(_processor))
			return;
		s_Listeners.Add(_processor);
	}

	public static void Unregister(DetectionProcessor _processor)
	{
		if (_processor == null)
			return;
		s_Listeners.Remove(_processor);
	}

	public static void ResetForTests()
	{
		s_Listeners.Clear();
		LastPublishDeliveryCount = 0;
	}

	public static void PublishGunshot(Transform _source, Vector3 _position)
	{
		Publish(SoundEvidenceMath.Create(_source, _position, SoundEventType.Gunshot));
	}

	public static void PublishExplosion(Transform _source, Vector3 _position)
	{
		Publish(SoundEvidenceMath.Create(_source, _position, SoundEventType.Explosion));
	}

	public static void PublishFootstep(Transform _source, Vector3 _position)
	{
		Publish(SoundEvidenceMath.Create(_source, _position, SoundEventType.Footstep));
	}

	public static void PublishImpact(Transform _source, Vector3 _position)
	{
		Publish(SoundEvidenceMath.Create(_source, _position, SoundEventType.Impact));
	}

	public static void Publish(in WorldSoundEvent _evt)
	{
		LastPublishDeliveryCount = 0;
		if (_evt.Source == null)
			return;

		float range = _evt.AudibleRangeMeters;
		if (range <= 0f)
			return;

		float rangeSq = range * range;
		Vector3 pos = _evt.Position;
		for (int i = s_Listeners.Count - 1; i >= 0; i--)
		{
			DetectionProcessor listener = s_Listeners[i];
			if (listener == null)
			{
				s_Listeners.RemoveAt(i);
				continue;
			}

			Vector3 origin = listener.transform.position;
			float dx = origin.x - pos.x;
			float dy = origin.y - pos.y;
			float dz = origin.z - pos.z;
			float distanceSq = dx * dx + dy * dy + dz * dz;
			if (!SoundEvidenceMath.IsAudible(distanceSq, rangeSq))
				continue;

			float confidence = SoundEvidenceMath.EvaluateConfidence(
				Mathf.Sqrt(distanceSq),
				_evt.Strength,
				range);
			if (confidence <= 0f)
				continue;

			LastPublishDeliveryCount++;
			listener.ReceiveWorldSound(in _evt, confidence);
		}
	}
	#endregion
}
