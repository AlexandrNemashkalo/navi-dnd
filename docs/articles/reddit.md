# Reddit-посты

Reddit плохо относится к одному посту, разосланному в 10 сабреддитов. Ниже два варианта: для разработчиков и для игроков. Публикуй с разницей в несколько дней и проверяй правила каждого саба про self-promotion.

---

## Вариант A — для разработчиков

**Куда:** r/csharp, r/dotnet, r/aigamedev, r/LocalLLaMA (только если будет поддержка локальных моделей), r/gamedev (по правилам — только в тред Screenshot Saturday или с флером)

**Title:**
I built a minimalist terminal D&D game in C# where an LLM is the Dungeon Master but never touches the dice. Looking for contributors

**Media:** первым вложением `docs/media/g4_combat_turn.gif`, затем `07_dice_disadvantage.png`, `g2_map_hover.gif`, `03_hero.png`.

**Body:**

For a while now I've been working on **NaviDnD**, a minimalist console RPG for Windows where Claude or OpenAI Codex runs a D&D 5e game. There's no 3D and no game engine: just one terminal window with an ASCII-style tactical map, a narration log and a character sheet. The GIF is a real combat turn from a fresh playthrough (the model wait time is cut).

The first thing you learn when you let an LLM be the DM: it's a great storyteller and a terrible game engine. Goblins with 2 HP left "rise again", doors move between turns, and the AI conveniently rolls a natural 20 whenever the scene needs drama.

So the core rule of the project is: **the model narrates and adjudicates. The code owns state, geometry, dice and progression.**

How that works in practice:

- **State changes are patches, not rewrites.** Each turn the model returns JSON with narration entries and patches to the world state. Collections are patched by index, so it can't accidentally "redraw" the dungeon. Some fields like `hero.level` and `hero.xp` are engine-only: the model can award XP for defeated monster keys, but the engine does the math.
- **The player rolls the dice.** The model calls a `roll_dice` MCP tool, the game shows the d20 animation with DC and modifiers, and the model gets `{roll, total}` back. The tool rejects fake rolls: no modifiers and no DC usually means the model is trying to roll for flavor.
- **The model plans maps, the code builds them.** `plan_location` describes the idea (dungeon/cave/outdoors), a generator builds the geometry, and chunks get populated as you explore. Walking around doesn't call the model at all.
- **No API key.** The game finds an installed Claude Code or Codex CLI, spawns it, and plugs in its own MCP server (dice, targeting, movement, monster/spell lookups, map gen). You play on the subscription you already have.
- **The UI is deliberately minimal: a console app** (Spectre.Console) with a custom region buffer and animation loop. Portraits and monster icons are SVGs rendered as Braille dots. No Unity and no asset pipeline, so contributing means writing plain C#.
- **Offline TTS** with Silero, bundled with portable Python, plus diff-patch auto-updates so players don't redownload 500 MB.
- **Testing nondeterminism:** 200+ logic tests, UI tests that drive the real process, and prompt tests against real models that check *consequences in state* (did the goblin actually lose HP?), not just whether the JSON is valid.

Stack: .NET 9, ~45k lines of C#, ~13 MCP tool groups, rules live in prompts and data files, so the engine itself isn't tied to D&D.

**Where I'd love help:**

- reducing latency (streaming entries as they arrive, fewer tool round-trips)
- trimming a ~59 KB main system prompt without breaking rules
- a cross-platform terminal layer (Linux/macOS)
- playtesters who know 5e rules and can tell me exactly where the DM cheats
- people interested in plugging in other TTRPG systems

Repo: https://gitlab.com/navitalevich/navi-dnd
Download (Windows x64): https://gitlab.com/navitalevich/navi-dnd/-/releases/permalink/latest

It's free and the source is open. The license is non-commercial: you can use, modify and share it, but not sell it. Happy to answer anything about the architecture in the comments.

---

## Вариант B — для игроков

**Куда:** r/rpg, r/DnD (обычно только с флером [OC]/[Resource] или в тредах для self-promo), r/AIDungeon, r/solorpgplay, r/Solo_Roleplaying

**Title:**
I made a free, minimalist terminal D&D 5e game where an AI is the DM, but the dice, HP and map are handled by real game code

**Media:** `docs/media/g3_intro.gif` первым, затем `07_dice_disadvantage.png`, `08_combat_crit.png`, `09_world_map.png`.

**Body:**

Hi! I've been building **NaviDnD**, a free game for Windows where you create a character and Claude or ChatGPT's Codex runs the adventure for you. It looks like a tabletop session in a terminal: a grid map drawn in text, a story log and your character sheet. Nothing flashy, so the story stays in focus.

What annoyed me about playing D&D in a chat window was that the AI forgets things and fudges everything. So here:

- **You roll your own d20** on screen, with the DC and modifiers shown. The AI can't roll for you.
- **HP, inventory, XP and leveling** are tracked by the game, not the AI's memory. Leveling up has its own screen; the code calculates HP, proficiency and ASI, and the AI only offers the choices the rules allow.
- **Tactical grid combat** with initiative, reactions, targeting and movement ranges.
- **Dungeons, caves and wilderness** generated as you explore, fog of war, darkvision, and a world map with travel time and random encounters.
- Quest journal, character sheet, bestiary.
- **Narrator voice** that works offline. English and Russian.

You need Claude Code or Codex CLI signed in (it uses your existing subscription, with no API key to set up). The game finds them automatically.

Download: https://gitlab.com/navitalevich/navi-dnd/-/releases/permalink/latest

It's a solo project and I'm looking for playtesters and contributors. Tell me where the DM breaks the rules, and I'll fix it.

---

## Ответы на вопросы, которые точно зададут

- **«Почему не локальные модели (Ollama)?»** — Сейчас модель должна надёжно вызывать много инструментов подряд. Поддержку локальных моделей через MCP-совместимый клиент можно обсудить, будем рады PR.
- **«Почему не GitHub?»** — [ответ; либо сделай зеркало на GitHub, на Reddit так проще найти проект и поставить звезду]
- **«Это open source?»** — Исходники открыты, но лицензия некоммерческая, это не OSI open source. Лучше сказать это прямо, иначе в комментариях поправят.
- **«Сколько стоит?»** — Игра бесплатная. Нужна подписка Claude или ChatGPT, которая даёт доступ к CLI.
