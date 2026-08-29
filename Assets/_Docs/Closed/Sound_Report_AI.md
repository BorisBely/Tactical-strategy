# Sound / Reports in AI Perception Frame

**Слой:** тактический **#9**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.2.  
**Статус:** **CLOSED / FROZEN 25.08.2026.** EditMode `SoundInAiTests` **PASS 18/0** (23:10); Play `SoundInAi_LAST.txt` **PASS 20/0** (23:05); Arena `Infantry_20260825_231643`. Report → Search перенесён в **#10** (`Search_2.md`). Этот слой не размораживать.

**#8 gate.** CombatEventHub остаётся фактом мира. `Combat_Event_World.md` **CLOSED / FROZEN**. #9 не превращает CombatEventHub в sound-шину.

## Три шины (не смешивать)

```text
CombatEventHub     = факт мира о combat event     → ImmediateThreat (#8/#7)
WorldSoundHub      = физическое распространение    → DetectionProcessor
DetectionProcessor = обработка perception
```

```text
SHOT
  ↓
WorldSoundHub → DetectionProcessor → SoundContact → AIPerceptionFrame → Tactical AI
                                                         ↓
                                              Defense / Attack → Search
                                              Idle → nothing

SHOT
  ↓
CombatEventHub → ImmediateThreatBridge → ImmediateThreat
```

Цепочки независимы. Один выстрел может дать **два следа**: combat fact + perceptual sound.

## Контракт SoundContact

Поля: `Source`, `Position`, `Type`, `Confidence`, `Time`, `Age`, `Hostile`.

```text
sound ≠ Observed
sound ≠ AimPoint
sound ≠ Identity commit
sound ≠ LastKnown
sound ≠ Fire
sound ≠ target
```

Звук не создаёт визуальный контакт в `AIPerceptionFrame` visual-каналах.  
Если визуальный контакт уже был и потерян, звук **не** подменяет `LastKnown`. Search по звуку идёт в `SoundPosition`.

Stage 16 (FROZEN): улика звука живёт на `PerceivedContact` (`SoundConfidence` / `SoundPosition`), не пишет Observed / AimPoint / Identity / LastKnown. #9 **копирует** это в отдельный канал снимка, не в Visual Contacts.

## Жизненный цикл

```text
EVENT → fresh sound → confidence → decay → expired
```

Горизонт и кривая — Stage 16 (`SoundKnowledgeMath`, 3 с). **Не ретюнить.**  
AI отличает свежесть по `Age` / `Confidence`. Отдельная сложная модель не нужна.

Для автономного Search «hostile sound» = `Hostile` и тип `Gunshot` или `Explosion`.  
Footstep / Impact — в канале звука, Search не стартуют.  
`SoundEventType.Unknown` в снимок не попадает.

## Канал Report

Независимо от Sound и Vision:

```text
Reporter, Subject, Position, reported Identity, Confidence, Time, Age
```

```text
Report ≠ Observed
Report ≠ AimPoint
Report ≠ Fire
Report ≠ «я вижу врага»
```

#9 кладёт доклад в snapshot. **Report → Search** — слой **#10** (`Search_2.md`), не этот срез.

## Канон тактики (только это)

```text
Defense + hostile combat sound  → Search (SearchPosition = SoundPosition)
Attack  + hostile combat sound  → Search (SearchPosition = SoundPosition)
Idle    + hostile combat sound  → nothing
Search  + new sound             → не дублировать Search, не сдвигать SearchPosition
Attack  + VisibleNow hostile    → звук не сбрасывает Attack
```

Visual Search по-прежнему: `LastKnown`. Sound Search: `SoundPosition`. Источники разные.

Не в #9: поворот на звук, огонь по звуку, цель по звуку, flank, cover, Search 2.0.

## Тесты

| Набор | Где |
|-------|-----|
| A–E Sound Contact / isolation / decay / snapshot / Search | EditMode `SoundInAiTests` **PASS 18/0** (25.08.2026 23:10) |
| #8 регрессия: CombatEvent ⊥ WorldSound | тот же набор + EditMode `CombatEventTests` |
| Play | `Tools/Tests/Run Sound In AI (Play)` → `SoundInAi_LAST.txt` **PASS 20/0** (25.08.2026 23:05) |
| Arena | `Infantry_20260825_231643` (23:16): 10 Player / 10 Enemy / 20 Civilian; SHOT 95; THREAT 20 только у боевых, **0 THREAT / 0 Search у Civilian**. t=36.783 SHOT + SOUND Gunshot `pos=(-5.4, 1.5, 86.9)`; t=36.883 Search `search=(-5.4, 1.5, 86.9)` при `hostileVis=0` — это SoundPosition, не LastKnown `(-5.2, 0.1, 88.4)`. Все 10+10 боевых вошли в Search; Footstep до первого выстрела Search не стартовал. |

Слой **FROZEN**. Поворот на звук, огонь по звуку — не этот слой. Report → Search — #10.
