# AI Use of Force Policy

**Статус: FROZEN** (Play 2026-08-20 00:38:44)  
Vision / AI-0 / AI-1 / G6 math остаются FROZEN. Этот слой не ретюнит Q, Memory, Identity и не меняет `EngagementDecisionMath`.

**Приёмка:** `UseOfForcePolicy_LAST.txt` **PASS 107/0**. `IK-GRIP-UNREACHABLE` — шум harness.

```text
AIPerceptionFrame  →  UseOfForceEvaluator  →  ForcePermission
UnitAIState        →  log only
UseOfForceLevel    →  evaluator

ForcePermission  →  EngagementDecisionController adapter
  Denied  →  Fire/Aim become Ignore; Track stays Track
  Allowed →  existing G6 (Unknown may Fire remains a G6 fact)

Allowed ≠ Fire
Attack ≠ fire
Policy ≠ UnitAIState
```

---

## 1. Пять уровней

| Level | Смысл |
|-------|--------|
| SelfDefense | сила только против Hostile при ImmediateThreat |
| RestrictedDefense | сила против Hostile (зона позже; матрица как MissionCombat) |
| MissionCombat | сила против Hostile |
| FullEngagement | сила против Hostile |
| NoFriendlyFire | сила против всех, кто не Friendly |

По умолчанию на новом `UnitAIController`: **SelfDefense**.  
Смена политики — любой уровень в любой, не лестница. `TrySetUseOfForcePolicy` **не** меняет `UnitAIState`.

RestrictedDefense vs MissionCombat в этом проходе: **одна и та же матрица Relationship**. Разница имени/контекста, без zone evaluator.

---

## 2. Матрица (Relationship, не Identity / не UnitTeam)

`ImmediateThreat` — **injected bool** на контроллере. Это не `ThreatLevel.High`.

| Policy | Friendly | Neutral | Unknown | Hostile без threat | Hostile + ImmediateThreat |
|--------|----------|---------|---------|--------------------|---------------------------|
| SelfDefense | NO | NO | NO | NO | YES |
| RestrictedDefense | NO | NO | NO | YES | YES |
| MissionCombat | NO | NO | NO | YES | YES |
| FullEngagement | NO | NO | NO | YES | YES |
| NoFriendlyFire | NO | YES | YES | YES | YES |

Порядок evaluator:

1. нет контакта → Denied / `NoContact`
2. `Relationship == Friendly` → Denied / `FriendlyProtected` (все уровни)
3. switch Level:
   - SelfDefense: Hostile+threat → Allow / `SelfDefenseImmediateThreat`; Hostile без → Deny / `SelfDefenseNoImmediateThreat`; иначе Unknown/Neutral deny
   - RestrictedDefense / MissionCombat / FullEngagement: Hostile → Allow / `PolicyAllowsHostile`
   - NoFriendlyFire: `Relationship != Friendly` → Allow / `NonFriendly` (не OR-цепочка Hostile/Neutral/Unknown)

---

## 3. Ownership

| Слой | Решает |
|------|--------|
| Perception (`AIPerceptionFrame`) | кто это по Relationship |
| State (`UnitAIState`) | какая задача (Idle/Defense/Attack/…) |
| Use of Force (`UseOfForceLevel`) | можно ли применять силу |
| G6 (`EngagementDecisionMath`) | Track / Aim / Fire / Ignore при **разрешённой** силе |
| Weapon | выстрел (не этот проход) |

Два юнита → две политики. Нет `static` глобального RoE. Debug-кнопки вызывают `UseOfForceSideCommands.Apply` на всех `UnitTeam` Player или Enemy.

`Unit.prefab` **не** печётся с `UnitAIController`. Overlay / smoke добавляют компонент в Play.

---

## 4. G6 handoff

Адаптер только в `EngagementDecisionController` (additive):

- нет `UnitAIController` → G6 как раньше (тесты G6 не добавляют AI)
- есть AI и Denied → `Fire`/`Aim` → `Ignore`; `Track` не трогать
- есть AI и Allowed → пробросить решение G6; **не** требовать Fire

Не редактировать `EngagementDecisionMath.cs` и G6-тесты (`UnknownIdentity_CanFire` остаётся).

---

## 5. Проверки

EditMode: `UseOfForcePolicyTests`.  
Play: `Tools/Tests/Run AI Use of Force (Play)` (`m_RunOnStart = false`).  
Отчёт: `UseOfForcePolicy_LAST.txt`.

Обычный Play: две debug-кнопки справа сверху — цикл политики всех юнитов игрока / всех юнитов врага.

```text
SelfDefense → RestrictedDefense → MissionCombat → FullEngagement → NoFriendlyFire → SelfDefense
```

**AI-1A FROZEN.** Дальше Navigation / Combat execution — не этот документ.
