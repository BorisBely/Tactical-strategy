# Сессия: Приоритеты стрелок направления (v2)

## Дата: 2026-07-18

## Идея по цветам

| Цвет | Режим | Смысл |
|---|---|---|
| Жёлтая | `TurnOverDistance` | Проверить направление; если никого нет — вернуться |
| Синяя | `HoldToEnd` | Держать сторону (фиксированный угол) |
| Зелёная | `LookAtPoint` | Держать точку (look-point) |

**Глобально:** маршрут не отменяется, агент не стопится. Спецскана сектора нет — только обычное зрение + `RequestImmediateScan` в момент полного доворота.

---

## Фазы (`ArrowPriorityPhase`)

| Фаза | Назначение |
|---|---|
| `None` | Нет активной логики стрелки |
| `Turning` | Доворот к центру стрелки (все цвета) |
| `YellowReturning` | Возврат к углу старой цели после жёлтой |
| `BlueHold` | Удержание стороны до конца маршрута |
| `GreenHold` | Удержание look-point до конца маршрута |

Дополнительно: `m_YellowDeferredActive` — мониторинг отложенного доворота жёлтой при потере цели на ходу (<5 м).

---

## Жёлтая

1. Поворот по стрелке → полный доворот (порог 5°).
2. `RequestImmediateScan` (обычное зрение).
3. Память только `m_OldTargetAngle` (если до активации была `VisibleTarget`).
4. После скана:
   - цель **в секторе жёлтой стрелки** → трек, `m_YellowDeferredActive = true`;
   - цели в секторе нет + был old angle → `YellowReturning` (держать угол старой цели **на ходу**, пока цель снова не видна);
   - иначе: стоит → держать угол стрелки; идёт → лицом к движению.
5. Память `m_OldTargetAngle` **не стирается** при повторной жёлтой на том же маршруте.
6. Потеря цели + идёт + <5 м → доворот к углу жёлтой → снова скан (ветка A).
7. >5 м → очистка без доворота.

---

## Синяя

- Центр = фиксированный угол стрелки.
- Живёт до **конца всего маршрута**.
- `VisibleTarget` → корпус на цель (sticky, любое отклонение).
- Цель пропала → обратно на угол стрелки.

---

## Зелёная

- Центр = направление на look-point (каждый кадр).
- Цель в пределах half-FOV от look-point → корпус на цель.
- Иначе → корпус на look-point.
- Режим не снимается при появлении цели.

---

## Сброс hold / жёлтого состояния

Синяя/зелёная hold и жёлтый deferred снимаются через `ResetActiveArrowFacingHold()`:

- Конец всего маршрута (очередь и waypoints пусты).
- `ClearWaypoints` / новый кликовый маршрут (`SetDestinationDirect`).
- `ClearCommandQueue` перед новым приказом движения.
- Нарисованный маршрут **без Shift** (`ClearWaypoints` перед enqueue).
- `IssueInPlaceFacingOrder` / `IssueDirectMoveOrderWithWait`.
- `IssueGroundLookCommand` на месте (`PerformInPlaceGroundFacing`).
- `HardStop`.
- Активация другой стрелки (`StartFacingTurn` → `ClearArrowPriorityState`).
- Fallen / dead.

**Не сбрасывает:** Shift+enqueue к существующему маршруту (hold до конца маршрута); переход между сегментами одного маршрута (`ShouldPersistFacingTurnAcrossQueuedCommand`).

---

## Изменённые файлы

| Файл | Изменения |
|---|---|
| `RtsUnitMember.cs` | State-machine стрелок; `IsManualBarrelFacingActive`; `IsFacingAngleReached` по стволу |
| `UnitClickToMove.cs` | `ResolveHorizontalFacingBodyYaw` для override |
| `UnitNavLocomotionDriver.cs` | то же |
| `UnitHorizontalFacingUtility.cs` | body↔barrel offset, barrel-centric yaw |
| `UnitWeaponAiming.cs` | body-align отключён при manual/arrow override |
| `UnitVision.cs` | engage forward = 100% bore; FOV по стволу при manual facing |

---

## Barrel-centric facing (high ready)

- `OverrideFacingAngle` и углы стрелок = **world yaw ствола** (линия огня).
- Корень компенсирует offset ready-позы: `bodyYaw = desiredBarrelYaw - bodyBarrelOffset`.
- Полный доворот стрелки проверяется по **стволу**, не по корню.
- `UnitWeaponAiming.AlignBarrelToBodyWhenReadyNoTarget` **выключен** при manual/arrow facing.

---

## Тест-кейсы

### Стрелки
1. Жёлтая без старой цели, стоит → доворот, скан, остаётся на стрелке.
2. Жёлтая без старой цели, идёт → доворот, скан, нет цели → лицом к движению.
3. Жёлтая со старой целью, пустой скан → возврат к old angle.
4. Жёлтая: нашёл цель → трек; потеря на ходу <5 м → доворот к жёлтой + скан снова; >5 м → без доворота.
5. Синяя на длинном маршруте: держит сторону через waypoint'ы; цель тянет корпус; потеря → обратно; новый маршрут сбрасывает.
6. Зелёная: корпус на look-point; цель в секторе → на цель; вне — на точку.

### Barrel align (PlayMode)
7. High ready + стрелка 90°: gizmo ствола совпадает с направлением стрелки (body↔barrel < 3°).
8. Engage в ready: `[ReadyMoveFacing]` body↔barrel < 5° при трекинге.
9. Low ready / без оружия: компенсация не применяется.
10. Reload / suppress ready (>90° turn): без осцилляции ствола, body-align не конфликтует с override.

---

## Debug: ArrowFacing (Editor / Development)

Фильтр Console: `[ArrowFacing`

| Флаг | По умолчанию | Назначение |
|---|---|---|
| `ArrowFacingDebug.LoggingEnabled` | true | Event-логи (фазы, override, post-scan) |
| `ArrowFacingDebug.PeriodicSnapshotEnabled` | true | Snapshot раз в ~2.5 с при активной стрелке |
| `ArrowFacingDebug.PeriodicSnapshotIntervalSeconds` | 2.5 | Интервал snapshot |

**Event-логи (без спама):** `PHASE`, `ARROW start`, `OLD_TARGET saved/clear`, `POST_SCAN`, `YELLOW_RETURN`, `OVERRIDE set/clear`, `LOCOMOTION[...] mode=...` (только при смене режима).

**Snapshot:** body/barrel/override/oldTarget ошибки, phase, target, speed.

Отключить snapshot, оставив events: `ArrowFacingDebug.PeriodicSnapshotEnabled = false`.
