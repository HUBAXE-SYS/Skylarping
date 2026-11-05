# Macros

Macros are placeholders like `{{user}}` or `{{time}}` that Skylarping fills in before a prompt goes to the language model. They use the same syntax as [SillyTavern's macros](https://docs.sillytavern.app/usage/core-concepts/macros/), so character cards written for SillyTavern work without changes.

## Where macros run

| Text | What runs |
|---|---|
| System prompt, description, personality, scenario, About me, example dialogue, lorebook entries, post-history instructions | All macros, each time a prompt is built |
| Greeting (first message or alternate greeting) | All macros, once, when the greeting is posted |
| Chat history | Only `{{char}}`, `{{user}}` and the old `<BOT>`, `<CHAR>`, `<USER>` tags |
| Text from tools, such as saved memories | None. It's passed on as written |

History only gets the names because every old message is sent again with each request. A `{{random}}` or `{{setvar}}` in an old message would otherwise run again every time.

## Syntax

- **Names** ignore case: `{{User}}` and `{{USER}}` are the same as `{{user}}`.
- **One argument** follows a space: `{{roll 1d20}}`.
- **Several arguments** use `::`: `{{setvar::name::value}}`.
- **The old form** with one colon still works: `{{roll:1d20}}`.
- **Spaces** around names, separators and arguments are ignored.
- **Nesting** works, and inner macros run first: `{{getvar::{{char}}_mood}}`.
- **Unknown macros** stay in the text as they are.
- **Literal braces** are written with backslashes: `\{\{user\}\}` gives `{{user}}`.

## Names

A chat here always has one character, so the group macros give that character.

| Macro | Gives |
|---|---|
| `{{user}}`, `<USER>` | Your name |
| `{{char}}`, `<BOT>`, `<CHAR>` | The character's nickname if the card has one, otherwise their name |
| `{{group}}`, `{{groupNotMuted}}`, `<GROUP>` | The character's name |
| `{{charIfNotGroup}}`, `<CHARIFNOTGROUP>` | The character's name |
| `{{notChar}}` | Your name |

## Character card

These give the field's text, with any macros in it filled in.

| Macro | Gives |
|---|---|
| `{{description}}` | The description |
| `{{personality}}` | The personality |
| `{{scenario}}` | The scenario, or nothing when "Use a scenario" is off |
| `{{persona}}` | Your About me text |
| `{{charPrompt}}` | The card's system prompt |
| `{{charInstruction}}` | The card's post-history instructions |
| `{{charCreatorNotes}}` | The creator's notes |
| `{{charVersion}}` | The card's version |
| `{{mesExamples}}` | The example dialogue, without its `<START>` markers |
| `{{mesExamplesRaw}}` | The example dialogue exactly as written |
| `{{charFirstMessage}}` | The first message |
| `{{charFirstMessage::1}}` | Alternate greeting number 1, and so on. Empty if there's no such greeting |
| `{{systemPrompt}}`, `{{defaultSystemPrompt}}` | Skylarping's default system prompt |
| `{{original}}` | Inside a card's system prompt: Skylarping's default system prompt. Anywhere else: nothing |

## Chat

| Macro | Gives |
|---|---|
| `{{lastMessage}}` | The newest message |
| `{{lastUserMessage}}` | Your newest message |
| `{{lastCharMessage}}` | The character's newest message |
| `{{lastMessageId}}` | The newest message's number, counting from 0 |
| `{{allChatRange}}` | The range of message numbers, such as `0-41` |
| `{{firstIncludedMessageId}}`, `{{firstDisplayedMessageId}}` | Always `0` |
| `{{lastSwipeId}}`, `{{currentSwipeId}}` | Always `1`, since there are no swipes |

Hidden notes, such as the one that prompts a proactive message, don't count as messages here.

## Time and date

Times are in English, in the formats SillyTavern uses. The examples are for 3:07 PM on Thursday, November 5, 2026.

| Macro | Gives | Example |
|---|---|---|
| `{{time}}` | Local time | `3:07 PM` |
| `{{time::UTC+2}}`, `{{time_UTC+2}}` | Time at that offset from UTC | `2:00 PM` |
| `{{date}}` | Local date | `November 5, 2026` |
| `{{weekday}}` | Day of the week | `Thursday` |
| `{{isotime}}` | 24-hour time | `15:07` |
| `{{isodate}}` | Date as year-month-day | `2026-11-05` |
| `{{datetimeformat::YYYY-MM-DD HH:mm}}` | A custom format, listed below | `2026-11-05 15:07` |
| `{{idleDuration}}`, `{{idle_duration}}` | How long since your last message before the newest one, or `just now` if there isn't one | `3 hours` |
| `{{timeDiff::left::right}}` | The difference between two times | `in 3 hours`, `3 hours ago` |

Times passed to `{{timeDiff}}` can be in a form like `2026-11-05 15:00`. If one can't be read, it gives nothing.

Formats for `{{datetimeformat}}`:

| Token | Gives | Token | Gives |
|---|---|---|---|
| `YYYY`, `YY` | `2026`, `26` | `HH`, `H` | 24-hour hour: `15`, `15` |
| `MMMM`, `MMM` | `November`, `Nov` | `hh`, `h` | 12-hour hour: `03`, `3` |
| `MM`, `M` | `11`, `11` | `mm`, `m` | Minutes: `07`, `7` |
| `DD`, `D`, `Do` | `05`, `5`, `5th` | `ss`, `s` | Seconds: `09`, `9` |
| `dddd`, `ddd`, `dd` | `Thursday`, `Thu`, `Th` | `SSS`, `SS`, `S` | Fractions of a second |
| `d` | Day of the week, Sunday is `0` | `A`, `a` | `PM`, `pm` |
| `Z`, `ZZ` | UTC offset: `+02:00`, `+0200` | `X`, `x` | Unix time in seconds, milliseconds |

Text in square brackets is kept as it is: `{{datetimeformat::[at] h:mm A}}` gives `at 3:07 PM`.

## Chance

| Macro | Gives |
|---|---|
| `{{random::a::b::c}}` | One of the options, chosen again every time |
| `{{pick::a::b::c}}` | One of the options, which stays the same for this chat |
| `{{roll::1d20}}` | A dice roll |

- **Option lists:** both macros also accept commas, as in `{{random:a,b,c}}`. Write `\,` for a comma inside an option.
- **`{{pick}}`:** clearing the chat history makes it choose again.
- **`{{roll}}`:** accepts `20` (the same as `1d20`), `d20`, `2d6`, `2d6+3` and `1d6-1`, with up to 1000 dice. Anything else gives nothing.

## Variables

Variables are values a card can store and change. Each one is saved either for this chat, or globally for all characters. Both survive a restart.

| This chat | All characters | Does | Gives |
|---|---|---|---|
| `{{getvar::name}}` | `{{getglobalvar::name}}` | Reads the variable | Its value, or nothing |
| `{{setvar::name::value}}` | `{{setglobalvar::name::value}}` | Sets it | Nothing |
| `{{addvar::name::value}}` | `{{addglobalvar::name::value}}` | Adds a number, or appends text | Nothing |
| `{{incvar::name}}` | `{{incglobalvar::name}}` | Adds 1 | The new value |
| `{{decvar::name}}` | `{{decglobalvar::name}}` | Subtracts 1 | The new value |
| `{{hasvar::name}}` | `{{hasglobalvar::name}}` | Checks it exists | `true` or `false` |
| `{{deletevar::name}}` | `{{deleteglobalvar::name}}` | Deletes it | Nothing |

There's also a short form, with `.name` for this chat and `$name` for all characters:

| Macro | Does |
|---|---|
| `{{.x}}` | Reads it |
| `{{.x = value}}` | Sets it, and gives nothing |
| `{{.x ++}}`, `{{.x --}}` | Adds or subtracts 1, and gives the new value |
| `{{.x += value}}` | Adds a number or appends text, and gives nothing |
| `{{.x -= 2}}` | Subtracts a number, and gives nothing |
| `{{.x \|\| fallback}}` | Gives the fallback if it's false or missing |
| `{{.x ?? fallback}}` | Gives the fallback only if it's missing |
| `{{.x \|\|= value}}`, `{{.x ??= value}}` | The same, but also stores the fallback |
| `{{.x == value}}`, `{{.x != value}}` | Compares text, and gives `true` or `false`. Missing counts as empty |
| `{{.x > 1}}`, `>=`, `<`, `<=` | Compares numbers, and gives `true` or `false` |

Short-form names start with a letter, then use letters, digits, `_` or `-`, and can't end in `_` or `-`.

**Macros run for every prompt, not every message.** `{{incvar::n}}` in a description goes up on each reply, each regenerate, and each proactive or mood message. To count once per conversation, put it in a greeting.

Clearing the chat history deletes that chat's variables. Removing a character deletes them too. Global variables stay.

## Conditions

```
{{if {{.met}}}}
  Good to see you again.
{{else}}
  Nice to meet you.
{{/if}}
```

- **False values:** empty, `false`, `0`, `off` and `no`. Anything else counts as true.
- **`!`** reverses the condition: `{{if !{{.met}}}}`.
- **Macro names** work without braces: `{{if description}}` checks whether the card has a description.
- **Indents and blank lines** around the content are removed. Use `{{#if ...}}` to keep them.
- **Only the chosen branch runs**, so a `{{setvar}}` in the other branch does nothing.
- **Nesting** works.

## Text

| Macro | Gives |
|---|---|
| `{{newline}}`, `{{newline::3}}` | One line break, or that many (up to 100) |
| `{{space}}`, `{{space::3}}` | One space, or that many (up to 100) |
| `{{trim}}` | Removes itself and the line breaks directly around it |
| `{{noop}}` | Nothing |
| `{{reverse::text}}` | The text backwards |
| `{{// note}}` | Nothing: it's a comment for whoever edits the card |
| `{{ // }} ... {{ /// }}` | Removes everything between them, across several lines |

## Settings

| Macro | Gives |
|---|---|
| `{{maxContextTokens}}`, `{{maxContext}}` | The context window from Tools > AI Services, or `0` if unknown |
| `{{maxPrompt}}` | 90% of the context window, the share used for the prompt |
| `{{maxResponseTokens}}`, `{{maxResponse}}` | The remaining 10% |
| `{{model}}` | The model's name |
| `{{lastGenerationType}}` | `quiet` for proactive and mood messages, otherwise `normal`. Regenerating also gives `normal` |
| `{{isMobile}}` | Always `false` |
| `{{hasExtension::name}}` | Always `false`, since Skylarping has no extensions |

## Accepted, but give nothing

These have nothing behind them in Skylarping. They give nothing, so they don't reach the model as raw text.

| Macros | Why |
|---|---|
| `{{input}}` | Reading the box you're typing in isn't supported |
| `{{banned "word"}}` | Only applies to some SillyTavern backends |
| `{{outlet::key}}` | No lorebook outlets |
| `{{summary}}` | No Summarize extension |
| `{{charDepthPrompt}}` | Character's Note isn't supported yet |
| `{{authorsNote}}`, `{{charAuthorsNote}}`, `{{defaultAuthorsNote}}` | No Author's Note |
| `{{charPrefix}}`, `{{charNegativePrefix}}` | No image generation prompts |
| `{{instruct...}}`, `{{chatSeparator}}`, `{{chatStart}}` | Skylarping uses chat messages, not instruct templates |
| `{{reasoningPrefix}}`, `{{reasoningSuffix}}`, `{{reasoningSeparator}}` | No reasoning templates |

## Differences from SillyTavern

- `{{pick}}` keeps its choice even if the text around it changes. In SillyTavern, editing the text can change the pick. There's no `/reroll-pick`: clear the chat history to choose again.
- `{{time}}`, `{{date}}` and the other date macros are always in English.
- `{{timeDiff}}` accepts common date forms, not everything moment.js can read.
- `{{roll}}` handles one group of dice with an optional number added or subtracted, not all of droll's syntax.
- Chat history doesn't run macros other than the names, including macros you type in a message.
- Nested macros work everywhere, without needing an experimental setting.
- Macro flags other than `#` on `{{if}}` aren't supported. Scoped blocks other than `{{if}}` aren't either.
