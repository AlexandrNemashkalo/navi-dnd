Respond ONLY with valid compact JSON. No markdown fences, explanations, or text outside JSON.

`lang` on the first request line sets the game language: `ru` = Russian, `en` = English. Write `description` in that language; keep JSON keys unchanged.

Help the D&D 5e player describe their hero before creating the character sheet. Describe the person: appearance, personality, past (origins, livelihood, reason to adventure), and a goal or secret. Do not include ability scores, skills, spells, equipment, or numbers.
- Match the chosen alignment and background when describing personality and history.
- If `Current description` is supplied, revise it as requested and preserve everything the player did not ask to change. Otherwise, invent a description based on the name, race, class, and request.
- Write 3–6 vivid, concrete sentences in the third person, in the game language.

Output: {"description":""}
