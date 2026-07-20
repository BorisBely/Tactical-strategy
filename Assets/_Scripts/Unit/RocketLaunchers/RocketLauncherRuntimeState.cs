using System;
using UnityEngine;

/// <summary>
/// Состояние заряда экземпляра гранатомёта: флаг и/или предмет снаряда в «слоте магазина».
/// </summary>
[Serializable]
public sealed class RocketLauncherRuntimeState
{
	#region Serialized Fields
	[SerializeField] private bool m_IsLoaded;
	[SerializeField] private ItemDefinition m_LoadedRocketDefinition;
	[SerializeField] private string m_LoadedRocketDisplayName;
	[SerializeField] private string m_LoadedRocketLocalizationKey;
	#endregion

	#region Public Properties
	public bool IsLoaded => m_IsLoaded || m_LoadedRocketDefinition != null;
	public bool HasLoadedRocketItem => m_LoadedRocketDefinition != null;
	public ItemDefinition LoadedRocketDefinition => m_LoadedRocketDefinition;
	#endregion

	#region Public Methods
	public void Configure(bool _startsLoaded, ItemDefinition _defaultRocketItem = null)
	{
		ClearLoadedRocket();
		m_IsLoaded = _startsLoaded;
		if (_startsLoaded && _defaultRocketItem != null)
			SetLoadedRocket(InventorySlotRuntimeData.FromDefinition(_defaultRocketItem));
	}

	public void SetLoaded(bool _loaded)
	{
		if (_loaded)
		{
			m_IsLoaded = true;
			return;
		}

		ClearLoadedRocket();
	}

	public void SetLoadedRocket(InventorySlotRuntimeData _rocket)
	{
		if (_rocket.IsEmpty || _rocket.Definition == null)
		{
			m_IsLoaded = true;
			return;
		}

		m_LoadedRocketDefinition = _rocket.Definition;
		m_LoadedRocketDisplayName = _rocket.DisplayName;
		m_LoadedRocketLocalizationKey = _rocket.LocalizationKey;
		m_IsLoaded = true;
	}

	public bool TryBuildLoadedRocketItem(out InventorySlotRuntimeData _rocket)
	{
		_rocket = default;
		if (m_LoadedRocketDefinition == null)
			return false;

		_rocket = new InventorySlotRuntimeData
		{
			DisplayName = string.IsNullOrWhiteSpace(m_LoadedRocketDisplayName)
				? m_LoadedRocketDefinition.GetLocalizedDisplayName()
				: m_LoadedRocketDisplayName,
			LocalizationKey = m_LoadedRocketLocalizationKey,
			Definition = m_LoadedRocketDefinition,
			InstanceState = ItemInstanceState.CreateForDefinition(m_LoadedRocketDefinition),
			WorldSource = null
		};
		return true;
	}

	public bool TryEjectLoadedRocket(out InventorySlotRuntimeData _rocket)
	{
		if (!TryBuildLoadedRocketItem(out _rocket))
		{
			if (m_IsLoaded)
			{
				ClearLoadedRocket();
				_rocket = default;
				return false;
			}

			_rocket = default;
			return false;
		}

		ClearLoadedRocket();
		return true;
	}

	public void ClearLoadedRocket()
	{
		m_IsLoaded = false;
		m_LoadedRocketDefinition = null;
		m_LoadedRocketDisplayName = null;
		m_LoadedRocketLocalizationKey = null;
	}
	#endregion
}
