# План: ПКМ-меню на маркере маршрута

## Структура меню

```
ПКМ по сегменту маршрута
├── Перезарядка          — выполняет перезарядку оружия на точке
├── Граната ▸            — подменю с типами гранат → отложенный бросок
├── Режим движения ▸     — подменю: Бег / Шаг / Присед → смена стойки/темпа
├── Поворот ▸            — подменю: Поворот / Пост.поворот / В точку
├── Пополнить магазины   — зарядка всех магазинов + перезарядка оружия
└── Остановка            — точка ожидания (wait point), как по Alt
```

## Порядок реализации

```
1 → 2 → 3 → 4 → 5 → 7 → 8a+8b → 6 → 9
```

---

## Этап 1. Разделение быстрого клика и протяжки на сегменте маршрута

**Файл:** `Assets\_Scripts\Unit\RtsUnitSelectionManager.cs`

### Изменения:
- Добавить поле `m_RouteMenuDragThresholdPixels = 15f` (рядом с `m_InPlaceFacingDragThresholdPixels`, строка ~25)
- В `HandleRightMouseDown()` (строка 3663): при попадании по сегменту **не** вызывать `BeginWaypointFacingEdit`, а запомнить позицию + флаг `m_IsRouteMenuPending`
- Убрать проверку `!IsAltHeld()` из условия — Alt больше не нужен для wait point при использовании меню
- В `HandleRightMouseUp()`: если drag < порога → показать `RouteInteractionMenuController`; если >= → `BeginWaypointFacingEdit()`

### Проверка:
- Выделить юнита, построить маршрут из нескольких точек
- ПКМ быстро кликнуть по линии маршрута (без движения мыши) → появляется меню
- ПКМ зажать и потянуть → стрелка поворота как раньше

---

## Этап 2. Создание RouteInteractionMenuController

**Файлы:**
- `Assets\_Scripts\UI\RouteInteractionMenuController.cs`
- `Assets\_Scripts\UI\RouteInteractionMenuAction.cs` (enum)

### Детали:
- Паттерн: копия `FallenUnitInteractionMenuController.cs` (482 строки)
- Singleton, `DontDestroyOnLoad`, sorting order ~31600
- Поддержка подменю: пункты с `▸` открывают вложенный список справа от главного меню
- Метод: `ShowForRoute(RtsUnitMember unit, int segmentIndex, Vector3 worldPoint, Vector2 screenPos)`
- Главное меню — 6 пунктов
- 3 подменю:
  - **Гранаты** — динамический список из `CharacterInventory` юнита
  - **Режим движения** — статический: Бег / Шаг / Присед
  - **Поворот** — статический: Поворот / Постоянный поворот / Поворот в точку
- Event: `ActionClicked(RouteInteractionMenuAction, RtsUnitMember, int segmentIndex, Vector3 worldPoint, object payload)`
- Закрытие: Esc, клик мимо меню, Pause

### Стиль кнопок:
- Шире и ниже чем в `FallenUnitInteractionMenuController`: `c_MenuMinWidth = 260f`, `c_ItemHeight = 26f`

### Проверка:
- ПКМ по линии маршрута → меню с 6 пунктами
- Навести на «Гранаты» → справа подменю со списком гранат юнита
- Навести на «Режим движения» → справа Бег / Шаг / Присед
- Навести на «Поворот» → справа Поворот / Пост.поворот / В точку
- Клик по любому пункту → меню закрывается, срабатывает ActionClicked
- Esc или клик мимо → меню закрывается

---

## Этап 3. Пункт «Остановка» (wait point)

### Используем существующую систему:
- В `RtsUnitSelectionManager.cs` уже есть `TryPlaceWaitPointForRouteTarget()` (строка 3428)
- `CollectWaitPointDescriptors()` — отрисовка иконок
- `RemoveWaitPointIcon()` — удаление по ПКМ
- `IsPointerOverWaitPointIcon()` — ховер

### Реализация:
- При выборе «Остановка» → вызвать `TryPlaceWaitPointForRouteTarget(unitIndex, segmentIndex, worldPoint)`
- Иконка wait point появляется на маршруте (как при Alt+клик)
- Никаких новых структур не требуется

### Проверка:
- ПКМ по маршруту → Остановка → иконка wait point на линии
- Запустить симуляцию → юнит останавливается на точке и ждёт
- ПКМ по иконке → удаляется

---

## Этап 4. Приказ «Режим движения» (Бег/Шаг/Присед)

### Новый файл:
- `Assets\_Scripts\Unit\LocomotionRouteOrder.cs`

```csharp
[System.Serializable]
public struct LocomotionRouteOrder
{
    public UnitClickToMove.MoveTier MoveTier;     // Walk / Run
    public LocomotionStance Stance;               // Standing / Crouch
    public int RouteWaypointIndex;
    public Vector3 WaypointPosition;
}
```

### В `RtsUnitMember.cs`:
- `List<LocomotionRouteOrder> m_LocomotionOrders`
- `AddLocomotionOrder(LocomotionRouteOrder order)`
- `TryRemoveLocomotionOrder(int waypointIndex)`
- `ShiftLocomotionOrdersAfterWaypointRemoved()` — сдвиг индексов
- `TryApplyLocomotionAtWaypoint()` — вызывается при достижении waypoint-а:
  - «Бег» → `MoveTier.Run` + `LocomotionStance.Standing`
  - «Шаг» → `MoveTier.Walk` + `LocomotionStance.Standing`
  - «Присед» → `MoveTier.Walk` + `LocomotionStance.Crouch`

### Визуальный маркер:
- Маленький значок на точке маршрута (цвет: голубой)
- При наведении — кнопка удаления (X)

### Проверка:
- ПКМ → Режим движения → Бег → голубой маркер на маршруте
- Запустить → юнит доходит до маркера → переходит на бег, дальше идёт бегом
- Аналогично Шаг и Присед

---

## Этап 5. Приказ «Перезарядка»

### Новый файл:
- `Assets\_Scripts\Unit\ReloadRouteOrder.cs`

```csharp
[System.Serializable]
public struct ReloadRouteOrder
{
    public int RouteWaypointIndex;
    public Vector3 WaypointPosition;
}
```

### В `RtsUnitMember.cs`:
- `List<ReloadRouteOrder> m_ReloadOrders`
- `AddReloadOrder()` / `TryRemoveReloadOrder()` / `ShiftReloadOrdersAfterWaypointRemoved()`
- `TryStartReloadAtWaypoint()` — вызывает `StartWeaponReload()`

### Маркер:
- Цвет: зелёный/бирюзовый

### Проверка:
- Расстрелять часть магазина у юнита
- ПКМ → Перезарядка → зелёный маркер
- Запустить → юнит доходит → анимация перезарядки → оружие заряжено → идёт дальше

---

## Этап 6. Приказ «Граната» (через меню)

### Используем существующую систему GrenadeRouteOrder:
- `GrenadeRouteOrder.cs` — уже есть
- `RtsUnitMember.cs`: `AddGrenadeOrder()`, `TryStartGrenadeOrderAtWaypoint()` — уже есть
- `UnitGrenadeThrowController.GetAvailableGrenadesFiltered()` — список гранат

### Реализация:
- Подменю «Гранаты» — динамически показывает доступные типы из инвентаря юнита
- При выборе типа:
  - Закрыть меню
  - Войти в `BeginAiming()` от точки маршрута (`m_IsRouteGrenadePlanning = true`)
  - `m_RouteGrenadePlanningOrigin = worldPoint`
  - ЛКМ → создать `GrenadeRouteOrder` → добавить юниту
  - ПКМ/F → отмена

### Доработать `TryStartGrenadeOrderAtWaypoint()`:
- При достижении точки: **остановиться**
- Проверить наличие гранаты в инвентаре
- Если есть → выполнить бросок (`ConfirmThrow`)
- После броска → продолжить движение по маршруту

### Проверка:
- Юнит с гранатами, построить маршрут
- ПКМ → Граната → выбрать тип (Frag/Smoke/Flash)
- Появляется прицел-парабола от точки маршрута
- ЛКМ по земле → маркер гранаты на маршруте
- Запустить → юнит доходит → останавливается → бросает гранату → идёт дальше
- Если гранату забрали из инвентаря до подхода — юнит просто проходит точку без броска

---

## Этап 7. Подменю «Поворот»

### Три варианта:
- **Поворот** → `BeginWaypointFacingEdit()` с mode=`TurnOverDistance`
- **Постоянный поворот** → то же с mode=`HoldToEnd`
- **Поворот в точку** → то же с mode=`LookAtPoint`

### Режим редактирования на маркере:
- Появляется стрелка (как при ПКМ+протяжка)
- ЛКМ → сохранить (`EndWaypointFacingEdit()`)
- ПКМ / F / Esc → отменить без сохранения

### В `Update()` RtsUnitSelectionManager:
- Если `m_IsEditingWaypointFacing`:
  - ЛКМ → `EndWaypointFacingEdit()`
  - ПКМ/F/Esc → отменить (уничтожить маркер, выйти без сохранения)

### Проверка:
- ПКМ → Поворот → Поворот → стрелка на маршруте
- Двигать мышь → стрелка вращается → ЛКМ фиксирует → юнит поворачивается на точке
- ПКМ → Поворот → Пост.поворот → стрелка (синяя) → ЛКМ → юнит держит направление весь сегмент
- ПКМ → Поворот → В точку → стрелка → ЛКМ → юнит смотрит в указанную точку пока идёт

---

## Этап 8. Приказ «Пополнить магазины» + новая механика зарядки

### 8a. Ордер на маршруте

**Новый файл:**
- `Assets\_Scripts\Unit\MagazineRefillRouteOrder.cs`

```csharp
[System.Serializable]
public struct MagazineRefillRouteOrder
{
    public int RouteWaypointIndex;
    public Vector3 WaypointPosition;
}
```

**В `RtsUnitMember.cs`:**
- `List<MagazineRefillRouteOrder> m_RefillOrders`
- `AddRefillOrder()` / `TryRemoveRefillOrder()` / `ShiftRefillOrdersAfterWaypointRemoved()`
- `TryStartRefillAtWaypoint()` → вызывает `m_MagazineLoadingController.TryStartLoadingAllMagazines()`

**Маркер:** оранжевый

### Проверка 8a:
- ПКМ → Пополнить магазины → оранжевый маркер

---

### 8b. Новая механика «зарядка всех магазинов»

**Файл:** `Assets\_Scripts\Shooting\UnitMagazineLoadingController.cs`

**Новый метод:** `TryStartLoadingAllMagazines()`

**Алгоритм:**

```
Фаза 1 — зарядка всех магазинов в сумке:
  for each BagItem:
    if is Magazine && needsAmmo && hasMatchingAmmoBox:
      TryStartLoadingMagazineFromAmmoBoxes(bagIndex)
      ждать событие LoadingStopped
      перейти к следующему

Фаза 2 — перезарядка оружия:
  if текущий магазин в оружии не полный:
    TryStartReload()
    ждать ReloadSequenceCompleted

Фаза 3 — дозарядка нового магазина в оружии:
  if магазин в оружии не полный и есть патроны:
    дозарядить через TryStartLoadingMagazineFromAmmoBoxes
```

**Детали реализации:**
- Флаг `m_IsLoadingAllMagazines`
- Счётчик текущего индекса магазина
- Цепочка через существующий event `LoadingStopped`
- После завершения всей цепочки → `ResumeMovement()` (продолжение маршрута)
- Юнит помечается как busy на время процесса

### Проверка 8b:
- Дать юниту 3 полупустых магазина в сумке + патронные коробки
- Частично расстрелять магазин в оружии
- ПКМ → Пополнить магазины → оранжевый маркер
- Запустить → юнит доходит → останавливается
- По очереди заряжает все 3 магазина в сумке (слышны звуки зарядки)
- Перезаряжает оружие (анимация смены магазина)
- Дозаряжает новый магазин в оружии если надо
- Встаёт и продолжает движение
- Инвентарь: все магазины полные, оружие с полным магазином

---

## Этап 9. Финальная интеграция

### Связать всё воедино:
- `RouteInteractionMenuController.ActionClicked` → методы в `RtsUnitSelectionManager`
- Обработка удаления/вставки waypoint-ов — сдвиг индексов всех типов ордеров
- Удаление маркеров ордеров через hover + X кнопку (как у facing arrows)
- Очистка ордеров при удалении юнита

### Краевые случаи:
- Меню при активном Tactical Pause
- Меню при открытом инвентаре
- Пересечение с grenade throw mode
- Пересечение с route edit mode (LMB)
- ПКМ по иконке wait point — удаление (уже работает)
- Esc / клик мимо — закрытие меню

### Проверка:
- Все маркеры корректно отображаются на маршруте одновременно (wait point + reload + locomotion + refill + grenade)
- При удалении waypoint-а LMB — все ордеры после него сдвигаются
- При наведении на маркер — кнопка X для удаления
- Esc во время любого режима редактирования — корректный выход

---

## Сводка новых файлов

| Файл | Назначение |
|---|---|
| `Assets\_Scripts\UI\RouteInteractionMenuController.cs` | Контроллер контекстного меню маршрута |
| `Assets\_Scripts\UI\RouteInteractionMenuAction.cs` | Enum действий меню |
| `Assets\_Scripts\Unit\LocomotionRouteOrder.cs` | Ордер смены стойки/темпа на waypoint |
| `Assets\_Scripts\Unit\ReloadRouteOrder.cs` | Ордер перезарядки на waypoint |
| `Assets\_Scripts\Unit\MagazineRefillRouteOrder.cs` | Ордер пополнения магазинов на waypoint |

## Сводка изменяемых файлов

| Файл | Что меняется |
|---|---|
| `RtsUnitSelectionManager.cs` | Порог меню, логика ПКМ, обработка ActionClicked, интеграция |
| `RtsUnitMember.cs` | Новые списки ордеров, методы Add/Remove/Shift/TryStart |
| `UnitMagazineLoadingController.cs` | Новый метод `TryStartLoadingAllMagazines()` |
| `UnitWeaponReloadController.cs` | Возможно, доработка цепочки после зарядки |
| `UnitGrenadeThrowController.cs` | Возможно, доработка `TryStartGrenadeOrderAtWaypoint()` |
