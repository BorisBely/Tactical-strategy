# Сессия: Реализация рисования маршрута (Door Kickers style)
## Дата: 2026-07-17

---

## Что сделано
Реализована механика рисования маршрута ЛКМ по аналогии с Door Kickers.
Все изменения — в одном файле: `Assets\_Scripts\Unit\RtsUnitSelectionManager.cs`

### Как работает
1. Выдели юнита(ов)
2. Зажми ЛКМ на выделенном юните и веди курсор — рисуется оранжевая превью-линия
3. Отпусти ЛКМ — точки упрощаются (Douglas-Peucker), waypoint'ы применяются через `RtsUnitMember.EnqueueWaypoint()`
4. **Backtrack-erase**: если вести курсор обратно вдоль нарисованной линии близко к предыдущим точкам — линия укорачивается, удаляя «задетые» участки
5. Shift при отпускании — маршрут добавляется к существующему (append вместо replace)
6. **Alt при отпускании** — первый waypoint нарисованного маршрута становится wait point (остановка)
7. F / Esc во время рисования — отмена
8. Короткий клик без драга — работает как обычное выделение (не ломает старую логику)

### Добавленные Serialized поля (в инспекторе, секция "Draw Route")
- `m_DrawRouteActivationPixels` (5px) — порог драга для активации рисования
- `m_DrawRouteSampleMinDistance` (0.5m) — минимальная дистанция между точками семплирования
- `m_DrawRouteSimplificationEpsilon` (0.4m) — эпсилон Douglas-Peucker
- `m_DrawRouteMinWaypoints` (1) — минимальное количество waypoint-ов (без учёта стартовой точки)
- `m_DrawRouteEraseDistance` (0.8m) — дистанция детекта стирания при обратном движении
- `m_DrawRouteEraseLookback` (3) — сколько последних точек не участвуют в поиске стирания

### Добавленные private поля
- `m_IsDrawingRoute` — активен ли режим рисования
- `m_DrawRouteUnitIndex` — индекс юнита для рисования (>= 0 означает pending)
- `m_DrawRouteStartScreen` — экранная позиция начала драга
- `m_DrawRoutePoints` (List<Vector3>) — собранные сырые точки
- `m_LastDrawSamplePoint` — последняя засемпленная точка
- `m_DrawRoutePreviewLine` (LineRenderer) — превью-линия
- `s_DrawRoutePreviewMaterial` (static Material) — материал для превью

### Добавленные методы (в #region Draw Route, после EndRouteDrag)
- `TryDetectDrawRouteStart()` — детект ЛКМ на выделенном юните
- `EnterDrawRouteMode(int)` — активация режима рисования
- `UpdateDrawRoute()` — семплинг точек по мере движения мыши
- `CommitDrawRoute()` — упрощение + batch enqueue waypoint'ов
- `CancelDrawRoute()` — отмена и очистка
- `SimplifyLine(List<Vector3>, float)` — Douglas-Peucker + удаление дубликатов
- `TryFindDrawRouteErasePoint(Vector3)` — поиск точки для стирания при backtrack
- `EnsureDrawRoutePreviewLine()` — создание LineRenderer для превью
- `UpdateDrawRoutePreviewLine()` — обновление позиций превью-линии
- `DestroyDrawRoutePreviewLine()` — уничтожение превью-линии

### Изменённые методы
- `HandleLeftMouseSelection()` — интегрирована логика pending → active → commit
- `HandleRightMouseCommand()` — блокировка RMB при рисовании
- `UpdatePathInteractions()` — блокировка path-ховеров при рисовании
- `HandleKeyboardCommands()` — F/Esc отменяют рисование
- `AbortActivePointerGestures()` — добавлен CancelDrawRoute()
- `CancelRouteEditInputState()` — добавлен CancelDrawRoute()
- `OnDestroy()` — добавлен DestroyDrawRoutePreviewLine()

---

## Архитектура проекта (кратко)

### Ключевые файлы
| Файл | Роль |
|---|---|
| `RtsUnitSelectionManager.cs` | Ввод, селекшн, маршруты, формации (~7250 строк) |
| `RtsUnitMember.cs` | RTS-обёртка юнита: waypoint'ы, command queue, facing (~4867 строк) |
| `UnitClickToMove.cs` | NavMesh-локомоция, MoveTier (Walk/Run/Sprint) |
| `FormationLayoutUtility.cs` | Расчёт формаций, Hungarian algorithm |
| `UnitFallenStateUtility.cs` | Проверки IsFallenOrDead, IsRtsControllable |

### Механика сложных маршрутов (RMB+Shift)
- `RtsUnitMember.EnqueueWaypoint()` — добавляет waypoint в m_Waypoints и m_CommandQueue
- `RtsUnitMember.TryInsertRouteWaypointAtSegment()` — вставка между сегментами
- `RtsUnitMember.ClearWaypoints()` — очистка всего
- Визуализация: LineRenderer `m_PathLine` у каждого юнита

### Модификаторы
- **Shift**: enqueue waypoints, ускорение камеры
- **Alt**: free-look камера, wait point placement
- **Ctrl**: toggle селекшн, hold-to-end facing
- **LMB**: селекшн, box-select, route edit drag, **рисование маршрута (новое)**
- **RMB**: move preview, commit orders, facing arrows

---

## Что можно улучшить / доделать

1. **Формации при групповом рисовании** — сейчас используется простое смещение позиций. Можно интегрировать `FormationLayoutUtility.BuildFormation()` для правильного разлёта.

2. **NavMesh-валидация** — точки не проверяются на попадание в NavMesh. Добавить `NavMesh.SamplePosition` в `UpdateDrawRoute()` и/или `SimplifyLine()`.

3. **MoveTier** — сейчас всегда Walk. Можно добавить определение темпа по длине сегмента или через модификатор.

4. **Undo** — отмена последнего нарисованного маршрута (Ctrl+Z).

5. **Facing arrows на waypoint'ах** — можно добавить автоматический разворот по направлению сегмента.

6. **Плавное превью** — сейчас превью-линия угловатая (raw sampled points). Можно рисовать сглаженную линию через `SimplifyLine` в реальном времени с бОльшим эпсилоном для превью.

7. **Конфликт с box-select от юнита** — если начать drag от невыделенного юнита, будет box-select, а не draw route. Это by design (box-select приоритетнее для невыделенных).

---

## План дальнейших работ (из ROUTE_MENU_PLAN.md)
```
1 → 2 → 3 → 4 → 5 → 7 → 8a+8b → 6 → 9

Этап 1. Разделение клика и протяжки на сегменте маршрута (ПКМ-меню)
Этап 2. RouteInteractionMenuController
Этап 3. Пункт «Остановка» (wait point)
Этап 4. Приказ «Режим движения» (Бег/Шаг/Присед)
Этап 5. Приказ «Перезарядка»
Этап 6. Приказ «Граната» (через меню)
Этап 7. Подменю «Поворот»
Этап 8. Приказ «Пополнить магазины»
Этап 9. Финальная интеграция
```
