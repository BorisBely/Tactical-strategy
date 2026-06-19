using UnityEngine;

/// <summary>
/// Связывает прогресс самолечения юнита с полосой в ячейке панели здоровья.
/// </summary>
public static class HealthStatusHealProgressBridge
{
	#region Private Fields
	private static UnitHealth s_ActiveHealth;
	private static int s_ActiveInjuryIndex = -1;
	private static float s_Progress01;
	private static bool s_IsActive;
	#endregion

	#region Public Methods
	public static void Report(UnitHealth _health, int _injuryIndex, float _progress01)
	{
		if (_health == null || _injuryIndex < 0 || !_health.TryGetInjury(_injuryIndex, out InjuryUiEntry injury))
		{
			Clear(_health);
			return;
		}

		if (injury.IsStabilized)
		{
			Clear(_health);
			return;
		}

		s_ActiveHealth = _health;
		s_ActiveInjuryIndex = _injuryIndex;
		s_Progress01 = Mathf.Clamp01(_progress01);
		s_IsActive = true;
		RefreshPanels();
	}

	public static void Clear(UnitHealth _health)
	{
		if (_health != null && s_ActiveHealth != null && s_ActiveHealth != _health)
			return;

		s_IsActive = false;
		s_Progress01 = 0f;
		s_ActiveInjuryIndex = -1;
		s_ActiveHealth = null;
		RefreshPanels();
	}

	public static void ApplyToSlot(HealthStatusSlotView _slot, UnitHealth _panelHealth)
	{
		if (_slot == null)
			return;

		bool show = s_IsActive &&
		            _panelHealth != null &&
		            s_ActiveHealth == _panelHealth &&
		            _slot.HasEntry &&
		            _slot.EntryData.InjuryIndex == s_ActiveInjuryIndex &&
		            !_slot.EntryData.IsStabilized;

		_slot.SetHealProgressVisible(show);
		if (show)
			_slot.SetHealProgress01(s_Progress01);
	}

	public static void RefreshPanels()
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return;

		bindings.ApplyHealProgressToHealthPanels();
	}
	#endregion
}
