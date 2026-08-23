# Tactical AI Roadmap

**Статус: FROZEN** (2026-08-20). Рабочий порядок пехоты после Vision Stage 9: `Пехота_дорожная_карта.md` (A–F). **Не открывать #7 и не возвращаться к AI**, пока не закрыты этапы A–E той карты, включая **A10** (новая стрельба/отдача начата, не закончена, сейчас стреляет неверно).  
Порядок слоёв тактики зафиксирован. Не открывать отряд, укрытия, utility / HTN / GOAP / BT, пока не закрыт одиночный исполнительный контур и внешние источники (приказы, RoE, звук).

Сначала закрыть исполнение **одного юнита**, затем внешние входы информации, затем группу.

```text
Decision  →  Command  →  State  →  Execution
```

Высокоуровневый выбор задачи стоит **над** этой машиной, не вместо неё.

Канонический номер — колонка «#» ниже. Проектные этапы исполнения 1–4 (Identity / CombatIntent / Search / Attack-Retreat-Flee) соответствуют #2, #3, #4, #5.

---

## Порядок

| # | Слой | Статус |
|---|------|--------|
| 1 | Vision | **FROZEN** |
| 2 | Identity | **FROZEN** |
| 3 | CombatIntent | **FROZEN** |
| 4 | Search locomotion | **FROZEN** |
| 5 | Attack / Retreat / Flee execution | **FROZEN** |
| 6 | Real game commands | **6.1–6.4 CLOSED** |
| 7 | ImmediateThreat + working RoE | не открыт |
| 8 | World combat events / sound | не открыт |
| 9 | Sound in AI perception | не открыт |
| 10 | Search expansion | не открыт |
| 11 | Command priority / cancellation | не открыт |
| 12 | Target selection + fire calibration | не открыт |
| 13 | Squad / Group | не открыт |
| 14 | Commander / formations | не открыт |
| 15 | Cover / tactical positions | не открыт |
| 16 | High-level tactical decision | не открыт |

Ближайшая последовательность тактики после perception A–E **и закрытого A10** (стрельба/отдача): **7 → 8 → 9**. До того — `Пехота_дорожная_карта.md`. Не прыгать к 13–16. Не чинить текущую кривую очередь через #7: новая отдача начата и не закончена.

Не сейчас (явно не открывать раньше своего номера): зоны RestrictedDefense как отдельный трек, радио как отдельный трек, слияние 18 м и 500 м, патруль как новое состояние «на всякий случай», Block D как свалка.

---

## Закрыто (#1–#5)

Vision, Identity, CombatIntent, Search locomotion, Attack/Retreat/Flee — контракты в `Assets/_Docs/Closed/`. Слои не ретюнить, чтобы «AI стал умнее».

#5 закрыл одиночное движение:

```text
Attack    → destination → Walk → Stop   (остаёмся Attack)
Retreat   → destination → Walk → Stop   (остаёмся Retreat)
Flee      → destination → Walk → Idle
Search    → LastKnown snapshot → Walk → 15 m → Stop   (остаёмся Search)
```

Отмена: `Exit` → `Stop`, затем новый `Enter` → `Walk`.  
Defense якорь **не** входит в #5 и **не** является следующим шагом.

---

## #6 — Реальные игровые приказы

### 6.1 — Контракт **CLOSED**

Внешний приказ ≠ состояние. Игровой вход один: `UnitAIController.IssueCommand(TacticalCommand)`. Строгая таблица, без Idle/Defense bounce.

```text
Test / Scenario
        ↓
TacticalCommand
        ↓
IssueCommand
        ↓
UnitAIState + Context
        ↓
существующие handlers
```

`IUnitTacticalCommand` / `TryIssue` остаются отладкой. `TryApplyCommand` — внутренний приказ машины (same-state не трогает context).

Контракт: `Closed/Tactical_Game_Command_Contract.md`.  
Приёмка: EditMode `TacticalCommandContractTests`; Play `TacticalCommandContract_LAST.txt`.

Не в 6.1: RTS, UI, группы, RoE, новые клетки таблицы, правка executors.

### 6.2 — GameCommandService  **CLOSED**

Production-канал: `GameCommandService.Issue(unit, TacticalCommand)` → `ITacticalCommandReceiver` → `IssueCommand`. Нет AI → `NoAI` (контроллер не создаётся). Мёртвый юнит → `InvalidUnit`. Overlay/арена по-прежнему `TryIssue`.

Источник приёмки: `DebugGameCommandSource` (`Source=Game`). Не RTS.

Контракт: `Closed/Game_Command_Source.md`.  
Приёмка: EditMode `GameCommandServiceTests`; Play `GameCommandSource_LAST.txt`.

Не в 6.2: RTS, UI, мышь, группы, авто-AI на префабе.

### 6.3 — RTS / игровой ввод  **CLOSED**

Один input-слой, две аудитории: выбранные игроки и все живые Enemy (debug). Оба шлют ту же `TacticalCommand` через `GameCommandService.IssueMany`. Нет Group AI. Обычный RTS RMB-ход в `Normal` не заменяется.

Контракт: `Closed/Game_Command_Input.md`.  
Приёмка: EditMode `GameCommandInputTests`; Play `GameCommandInput_LAST.txt`.

Не в 6.3: box-select rewrite, Action Panel production UX, формации как Group AI, сеть, командир, подмена overlay, #7, авто-AI на префабе.

### 6.4 — Сквозная стабилизация  **CLOSED**

Приёмка всего command layer: замена задачи, Cancel, живой collect, изоляция сторон. Вход Play — `GameCommandInput.ConfirmPoint`, не `DebugGameCommandSource`. Combat isolation (не Fire / не цель); SHOT — #7. Defense не ходит.

Контракт: `Closed/Game_Command_Layer.md`.  
Приёмка: EditMode `GameCommandLayerTests`; Play `GameCommandLayer_LAST.txt`.

#6 больше не открывать. **A3 CLOSED / VERIFIED PASS 21/0**. **A4+A5 CLOSED / VERIFIED PASS 30/0**. **A6+A9 CLOSED / VERIFIED PASS 35/0**. **A7 CLOSED / VERIFIED PASS 31/0** (`Пехота_дорожная_карта.md`). **Stage 15 Attention (B): CLOSED / VERIFIED PASS 44/0**. Тактическое **#7** — после этапов A–E той карты, включая закрытый **A10** (стрельба/отдача не закончена).

Не добавлять второй игровой путь рядом с `GameCommandService`. `TryIssue` игрой не вести.

---

## #7 — Рабочий RoE / ImmediateThreat

`ImmediateThreat` сейчас мёртвый вход: его никто не выставляет, поэтому `SelfDefense` в игре почти всегда «не стрелять».

```text
входящий огонь / подтверждённая атака
        ↓
ImmediateThreat
        ↓
RoE
        ↓
Allow / Deny Aim/Fire
```

**ImmediateThreat не вызывает Fire.** Он только разрешает силу.

После слоя должны различаться на практике:

```text
SelfDefense
RestrictedDefense
MissionCombat
FullEngagement
```

а не только имена политик. Существующий Use-of-Force math не ретюнить без отдельной приёмки.

---

## #8 — Combat Event / Sound

Звуковой API есть, в боевой мир события почти не отправляются.

```text
Weapon fires
    ↓
Combat event
    ↓
Sound perception
    ↓
Contact / LastKnown
```

Инварианты:

```text
звук ≠ Vision
звук ≠ Observed
звук ≠ AimPoint
```

Звуковую память не смешивать с визуальной.

---

## #9 — Звук в кадре тактического AI

Отдельный этап от #8. Боевой контур может начать учитывать звук раньше, чем тактический AI.

```text
AI perception snapshot
 ├─ Visual
 ├─ Sound
 └─ Reports
```

Поведение звукового контакта (зафиксировать тестом, не импровизировать в коде):

```text
Defense + heard hostile  → Search
Attack  + heard hostile  → Search
Idle    + heard hostile  → ничего
```

Звук не становится автоматической стрельбой.

---

## #10 — Investigation / Search 2.0

Только после рабочего звука. Не новое состояние. Более содержательное исполнение **существующего** Search:

```text
SearchPosition
+
SearchArea
+
несколько точек поиска
```

Не добавлять сектора, прочёс и «умный поиск».

Базовый Search остаётся:

```text
LastKnown snapshot → Walk → 15 m → остановка → Found / stale
```

---

## #11 — Отмена / приоритет приказов

Когда появятся реальные команды, нужен единый контракт приоритетов. Порядок — **правила игры**, не заглушка в коде. Черновик для утверждения на старте этапа:

```text
Flee > Retreat > Attack > Search > Defense > Idle
```

Явно решить и покрыть тестами:

- новый приказ во время Search;
- Flee во время Attack;
- можно ли отменить Flee;
- повторный приказ того же состояния;
- что происходит с контекстом.

Это продолжение уже зафиксированной таблицы переходов, не вторая машина.

---

## #12 — Выбор цели и калибровка боя

Открывать только после #6–#11. TargetSelector до этого не ретюнить.

Измерять:

```text
кого выбирает
как часто меняет цель
как долго держит цель
как далеко стреляет
как часто стреляет по Unknown
```

Затем отдельно:

```text
TargetScore
Aim
Fire discipline
Retain = ResolvedMaxRange
```

---

## #13 — Squad / Group

До #12 отряд не трогать. Группе нужен стабильный одиночный исполнитель.

```text
Squad
 ↓
Orders
 ↓
Individual AI
 ↓
Movement / Combat
```

```text
Squad
 ├─ leader
 ├─ members
 ├─ squad destination
 └─ squad objective
```

Боевую логику внутрь отряда не копировать.

---

## #14 — Commander / Formation

```text
Commander → Squad → Unit
```

Формации, построение, общая точка атаки, отход, распределение задач, командирские приказы.

---

## #15 — Cover / Position Selection

Только после группы. «Найти укрытие» — выбор позиции, не ещё одна навигация.

Укрытия, позиции, сектора, перераспределение.

---

## #16 — Higher-level Tactical Decision

Utility / HTN / GOAP / Behaviour Tree — только над готовой машиной.

```text
Decision → Command → State → Execution
```

Не заменяет #5–#11.

---

## Патруль

Старый patrol — параллельная система, не состояние AI. Не добавлять состояние `Patrol` автоматически.

Сначала проверить, хватает ли:

```text
Idle / Defense / Attack
```

плюс приказы #6. Отдельный режим — только если патруль действительно самостоятельная задача.

---

## Дыры, которые карта закрывает по очереди

| Дыра | Слой |
|------|------|
| ImmediateThreat мёртвый | #7 |
| Три уровня RoE ведут себя одинаково | #7 |
| Звук/радио не в боевом мире | #8, потом радио отдельно не раньше #9 |
| AI-кадр без звука | #9 |
| Search слишком простой для расследования | #10 |
| Нет приоритета приказов | #11 |
| Патруль живёт отдельно | после #6, проверка на #11 |
| Выбор цели не калиброван | #12 |
| Нет отряда / командира / укрытий | #13–#15 |

---

## Правило открытия следующего слоя

Не начинать N+1, пока N не закрыт приёмкой (EditMode + Play, как #4/#5) и не помечен FROZEN.  
Не открывать #13–#16, чтобы «сразу было видно группу».
