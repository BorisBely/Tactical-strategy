using System;
using System.Collections.Generic;

/// <summary>
/// #8 world combat bus. Publishes facts. Does not write perception, identity, or Fire.
/// Separate from <see cref="WorldSoundHub"/> (that fans out into <see cref="DetectionProcessor"/>).
/// </summary>
public static class CombatEventHub
{
	#region Private Fields
	private static readonly List<Action<CombatEvent>> s_Listeners = new List<Action<CombatEvent>>(32);
	#endregion

	#region Public Properties
	public static int ListenerCount => s_Listeners.Count;
	public static int PublishCount { get; private set; }
	public static int LastListenerDeliveryCount { get; private set; }
	public static CombatEvent LastPublished { get; private set; }
	#endregion

	#region Public Methods
	public static void Subscribe(Action<CombatEvent> _listener)
	{
		if (_listener == null || s_Listeners.Contains(_listener))
			return;
		s_Listeners.Add(_listener);
	}

	public static void Unsubscribe(Action<CombatEvent> _listener)
	{
		if (_listener == null)
			return;
		s_Listeners.Remove(_listener);
	}

	public static void ResetForTests()
	{
		s_Listeners.Clear();
		PublishCount = 0;
		LastListenerDeliveryCount = 0;
		LastPublished = default;
	}

	public static void Publish(CombatEvent _evt)
	{
		LastPublished = _evt;
		PublishCount++;
		ImmediateThreatCombatEventBridge.Handle(_evt);

		LastListenerDeliveryCount = 0;
		for (int i = s_Listeners.Count - 1; i >= 0; i--)
		{
			Action<CombatEvent> listener = s_Listeners[i];
			if (listener == null)
			{
				s_Listeners.RemoveAt(i);
				continue;
			}

			LastListenerDeliveryCount++;
			listener(_evt);
		}
	}
	#endregion
}
