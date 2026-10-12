# Reddit: «смотрите, что я сделал» (англ.)

Медиа — `docs/media/en/`. Видео сверху поста: вкладка **Images & Video** → `combat_turn.mp4`, текст — в поле Body.
Пометки `[ … ]` — места для файлов (в редакторе Reddit: курсор на место пометки → значок картинки → файл), сами пометки удалить.

---

**Title:** I built a D&D game where an AI is the DM, but the game won't let it cheat

---

Hey all. For the last few months I've been working on a side project and I think it's finally at the point where other people could enjoy it, so here it is.

It's a single-player D&D 5e game for Windows where the Dungeon Master is an LLM (Claude or OpenAI Codex). The twist is that the AI only does the storytelling part. Everything that needs to be *correct* is handled by the game itself: the map, movement, line of sight, initiative, dice, HP. And yes, it runs in a console window. The graphics are drawn with braille characters, which honestly looks way better than I expected.

[combat_turn.mp4 — если видео уже сверху поста, здесь ничего не нужно]

**Why I made it**

I love D&D, but my group manages maybe one session a month. I tried playing with a chatbot a few times and it was fun for about twenty minutes. Then it forgot where the goblins were, handed me 500 gold for no reason and let me hit an AC 18 orc with a 6.

So the idea was simple: let the AI do what it's good at (improvising, NPC voices, describing stuff) and let normal code do what it's good at (remembering state, geometry, math).

**What's in it right now**

- Create a hero yourself or describe one and let the DM fill in the sheet
- Generate a world, then travel on a world map (food, time and random encounters on the road)
- Dungeons, caves and outdoor areas get generated as you explore. The AI plans what's there, the code builds the actual layout
- Grid combat. Monsters come from a monster manual, so a goblin has goblin stats, not whatever the model feels like today
- Dice are real. When the DM needs a roll you see the d20 spin on screen, and it can't fudge it afterwards
- Fog of war, darkvision, torches, stealth and ambushes
- A journal with quests, NPCs, places and a bestiary of monsters you've identified
- Optional offline narrator voice

[world_map.png]
[dice_roll.png]

**The part I'm proudest of: leveling up**

I just added XP and levels, and it was surprisingly tricky. The DM awards XP when you win a fight or finish a quest, but it is not allowed to level you up or touch your stats. When you have enough XP a LEVEL UP button shows up on your character sheet. You pick HP (average or one roll, no rerolls), ability scores, subclass, new spells, whatever your class gets at that level. Your sheet shows every change in gold before you accept.

And if you don't like the options, you can just type "I want Misty Step instead of Shield". The DM will either add it as an option (if the rules allow it at your level) or tell you why not. It can never just rewrite your character.

[level_up.mp4]

**Stuff that went wrong along the way**

- Early on the DM kept "forgetting" that enemies were dead and would happily attack me with a goblin I'd killed two turns before. Now dead monsters leave a body on the map and get removed from the turn order by the game, not by the AI.
- Last week I shipped a bug where clicking the shortcut a few times opened like a dozen copies of the game, each loading the voice model. My PC did not enjoy that. Fixed now, only one copy can run.

**Honest caveats**

- Windows only for now
- You need your own Claude Code or Codex CLI login. The game doesn't come with an API key and doesn't charge anything, but every turn uses your own subscription limits
- It's a hobby project, so expect rough edges
- Free for non-commercial use

Download: https://gitlab.com/navitalevich/navi-dnd/-/releases/permalink/latest

I'd really like feedback, especially from people who've tried other AI DM tools. What broke immersion for you? That's exactly the kind of thing I'm trying to fix.
