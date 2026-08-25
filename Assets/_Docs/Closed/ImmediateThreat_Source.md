# ImmediateThreat Source

**Слой:** тактический **#7**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.1.  
**Статус:** **CLOSED 24.08.2026** — A **PASS 10/0**; B `UseOfForcePolicyTests` **PASS 11/0**; C **PASS 3/0**; Play D **PASS 18/0**; E Use of Force Play **PASS 107/0**, Combat Engage **PASS 36/0**.

**Реализация (на диске).** `ImmediateThreatSource`, `ImmediateThreatSignal` (IncomingFire / ConfirmedHit / HostileAttack), TTL, UnitTeam hostility. С **#8 CLOSED** hitscan публикует `CombatEvent` (Gunshot/Hit); `ImmediateThreatCombatEventBridge` читает эти факты и вызывает тот же Signal. Внешний API Signal **не менялся**. Канал `THREAT` на смене; SNAP `immediateThreat` / `threatSource` / `threatAge`. Меню Current: `Tools/Tests/Run Immediate Threat Live (Play)` → `Assets/_Docs/Logs/Tests/ImmediateThreatLive_LAST.txt`. Combat Engage T3b: SelfDefense + incoming fire → Allow.

## Контракт

```text
ImmediateThreat = true  ⇔ этот юнит получил враждебную атаку по себе (окно TTL ещё живо)
ImmediateThreat ≠ ThreatLevel.High
Allow ≠ Fire
RoE не выбирает цель и не вызывает Fire
Threat не глобальный
```

Кто пишет флаг: только `ImmediateThreatSource`.  
Кто читает: `UseOfForceEvaluator` через `UnitAIController.ImmediateThreat`.

| Источник | Правило |
|----------|---------|
| IncomingFire | Выстрел, `TargetSelector.SelectedTarget` = этот юнит, атакующий Hostile по UnitTeam |
| ConfirmedHit | Hitscan попал в `DamageableTarget` этого юнита, атакующий Hostile |
| HostileAttack | Явный API; других типов в #7 нет |

TTL: `ImmediateThreatDuration` на компоненте (не канон). Повтор обновляет окно.

## Тесты

| Набор | Где |
|-------|-----|
| A1–A9 источник | EditMode `ImmediateThreatSourceTests` **PASS 10/0** (24.08.2026) |
| B матрица RoE | EditMode `UseOfForcePolicyTests` **PASS 11/0** (24.08.2026) |
| C G6 veto | EditMode `ImmediateThreatRoeHandoffTests` **PASS 3/0** (24.08.2026) + Play Combat Engage T3/T3b **PASS 36/0** |
| D1–D6 live | Play `Tools/Tests/Run Immediate Threat Live (Play)` → `ImmediateThreatLive_LAST.txt` **PASS 18/0** (24.08.2026) |
| E frozen regression | Play Archive Tactics: Use of Force **PASS 107/0** (24.08.2026 23:56); Combat Engage **PASS 36/0** (24.08.2026 23:48) |
| F четыре стороны | в том же Play D |

## Логи

Канал `THREAT` только на смене. SNAP: `immediateThreat` `threatSource` `threatAge`.
