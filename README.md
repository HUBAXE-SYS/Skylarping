# Skylarping

## About

Skylarping is an AI character chat application modeled after Skype 4-6. AI features are provided by an OpenAI-compatible endpoint, though TTS has only been tested with MOSS-TTS-Local via [Lemonade Server](https://github.com/lemonade-sdk/lemonade).

The application supports Windows XP SP3 or higher with .NET Framework 4.0 installed.

Skylarping is a fork of [Skymu](https://github.com/TheSkymuTeam/Skymu).

## Features

- **SillyTavern Character Card Format** - SillyTavern-formatted character cards can be imported/exported (except .WEBP format).
- **Text Messaging and Voice Calling** - Characters can be messaged via text or called via voice (voice calls require a TTS model capable of voice cloning).
- **Group chats and calls** - Characters can be added to group chats, where they can be messaged or called.
- **Tools calling** Characters can use [SimpleLLMChat](https://github.com/randomNinja64/SimpleLLMChat)-compatible tools. For more info, see [Tools](#tools).

## Requirements

- Windows XP SP3
- .NET Framework 4.0+
- An OpenAI-compatible LLM (and STT/TTS if desired) server

## Installation/Setup

Download the latest installer from releases. Install and run the application.

On first launch, sign in with any username.

Proceed through the onboarding wizard. Settings can later be changed via Tools > AI Services.

Skylarping stores its data in `%AppData%\Skylarping\<profile>`. Each character has a folder there with their card, picture, voice sample and memories. Chat history is in the profile's database.

## Building

The project can be built via the included `build.bat` script (Visual Studio/Visual Studio Build Tools/MSBuild required).

Alternatively, the project can be built using the `Skylarping.sln` file directly in Visual Studio.

Both methods require the .NET 4.0 Targeting Pack to be installed.

`build.bat` stages the release in `Skylarping-XP\` and, if [NSIS](https://nsis.sourceforge.io) is installed, packages that folder into `Skylarping Installer.exe` using `NSIS\SkylarpingSetup.nsi`. Without NSIS, the installer step is skipped with a warning.

**Note**: Builds generated using these methods include only the core executables by default. Optional binaries (`curl.exe`, `curl-ca-bundle.crt`) are packaged when present under `deps\` or can be manually placed beside `Skylarping.exe`. SimpleLLMChat tools are packaged with builds when placed in `deps\tools\`. Third-party license texts placed under `THIRD_PARTY_LICENSES\` are copied into the release folder as `THIRD_PARTY_LICENSES\` when present.

## Configuration/Options

AI configuration is handled via Tool > AI Services. The options available are listed below.

**Tools > AI Services**

- **Language Model**
  - **LLM Server** (http(s)://ip:port/v1 or http(s)://url/v1) - URL for your AI endpoint.
  - **API Key** - API key (if required by model provider)
  - **Model** - Model to use
  - **Context Window** - If known, can be set here to fill the context with recent messages. If `0` (unknown), the last 50 messages are sent.
  - **Reasoning** - Amount of reasoning performed by a model (sent as `reasoning_effort` in API requests). Supported values: **Off**, **Low**, **Medium**, **High**, **Model default** (default: Off). **Model default** uses the API default.
- **Chat Display** (also available via Options > Chats & SMS)
  - **Format Messages with Markdown** - Renders Markdown formatting in chat
  - **Show Replies as They're Being Written** - Streams replies in real-time (default: off)
  - **Show Edit Message and Remove Message** - Shows these commands in the message menu (default: on)
  - **Show Regenerate Reply** - Shows this command in the message menu (default: on)
- **Speech-to-Text**
  - **Speech-to-Text Server** (http(s)://ip:port/v1 or http(s)://url/v1) - URL for your speech-to-text endpoint (blank = use **LLM Server** and its **API Key**)
  - **API Key** - API key (if required by model provider)
  - **Model** - Model to use
  - **Language** - Language code such as `en` or `de` (blank = detect each time)
  - **Pause That Ends Your Turn** - Length of pause in milliseconds. Lower values reply sooner, but may cut you off mid-sentence.
  - **Speech Detection** - Detection level for speech: **Quiet room**, **Normal room**, **Noisy room**, or **Very noisy room** (default: Noisy room)
- **Text-to-Speech**
  - **Text-to-Speech Server** (http(s)://ip:port/v1 or http(s)://url/v1) - URL for your text-to-speech endpoint (blank = use **LLM Server** and its **API Key**)
  - **API Key** - API key (if required by model provider)
  - **Model** - Model to use (blank = characters stay silent on calls)
  - **Speed** - Speech speed as a percentage, from `50` to `200` (default: `100`)
  - **Let Me Interrupt the Character by Talking** - When enabled, talking interrupts the character. If a character keeps cutting themselves off, turn this off (default: on).
- **Group Chats**
  - **Direct Questions and Answers to Who They're For** - When a character asks a question, the character they named answers it. If nobody is named, the previous character speaker answers. An answer that isn't another question ends the exchange (default: on).
- **Tools**
  - **Max Rounds of Tool Use** - Maximum number of tool rounds in one reply, after which the character has to answer (default: `8`)
  - **External Tool Settings** - If SimpleLLMChat tools are installed, their configuration options will be available here.
  - **Timeouts** - Timeout for each tool call, in seconds; `0` means no timeout.

General "Skype" options are avaialable via Tools > Options. A list of these settings are below.

**Options**

- **General**
  - **My Picture** - Your profile picture
  - **About Me** - What your characters know about you
  - **Auto-Away** - Sets your status to Away after a period with no input (default: on, `5` minutes)
  - **Double-Click Starts a Call** - Starts a call when a contact is double-clicked (default: off)
- **Audio Settings** - Microphone, speaker and ringing devices
  - **Buffer on Calls** - How far ahead the speakers are fed, in milliseconds (blank = automatic: `300` on Windows XP, `150` otherwise)
  - **Device Sample Rate on Calls** - Both devices are opened at this rate and Skylarping converts, so Windows doesn't have to (default: Automatic = `48 kHz`, falling back to `44.1 kHz`). Choose `44.1 kHz` only for a sound card that can't run at 48 kHz.
  - **Write a Log of Each Call** - Writes timing information (no audio) to `%AppData%\Skylarping\logs` for echo troubleshooting (default: off)

Character editing is handled via right-clicking a character and choosing the **Edit Character** option. A list of available options is below.

**Edit Character**

- **Greet Me When a New Chat Starts** - Sends the first message when a new chat starts (default: off)
- **Use a Scenario** - Adds the scenario to every chat (default: off)
- **Message Me on Their Own** - Sends a message after a random number of minutes of quiet (default: off, `30` to `120` minutes). Stops after a maximum number of unanswered messages (default: `3`).
- **Update Their Mood Message on Their Own** - Changes the character's mood message after a random number of hours (default: on, `4` to `8` hours)
- **No Action Text** - Removes `*smiles*`-style actions from replies. Always on during calls (default: off).
- **Use the Lorebook** - Enables the character's lorebook (default: on)
- **Voice** - WAV or MP3 voice sample of up to 30 seconds, or a description to create one (OpenMOSS models only). The sample is sent to your text-to-speech server on calls.
- **Tools** - List of available tools and their enabled/disabled state for this character
- **More Card Fields** - System prompt, post-history instructions, group chat summary, talkativeness (default: `0.5`), creator's notes, tags, creator and version

**Edit Group** (right-click a group)

- **Members** - Characters in the group. Adding or removing a member posts a notice in the chat.
- **Talk on Their Own When the Chat Goes Quiet** - Characters talk after a random period of quiet (default: on, `10` to `30` minutes). Stops after a maximum number of rounds with no answer (default: `3`; `0` = no limit). Paused while your status is Do Not Disturb.
- **When Characters Reply to Each Other** - Wait before each reply (default: `15` to `60` seconds) and maximum number of replies in a row (default: `0` = no limit)
- **On Calls** - Pause between speakers (default: `1` to `3` seconds) and how long a call stays quiet before characters talk (default: `20` to `45` seconds)

## Adding and Chatting with Characters

- To add a character, select Add Contact. Fill in the form, or select "Import Card..." to fill it from a character card.
- To export a character, right-click the contact and select "Export Character...".
- To send a message, type it into the input box and press `Enter`.
- To edit or remove a message or regenerate a reply, right-click the message.
- To get a specific character's reply in a group, type `@` and select them from the list, or write their name.
- To talk to a character or group, start a call from their contact or chat.

## Tools

To install a package, extract it to the `tools` folder next to `Skylarping.exe`, or put it in `deps\tools\<package>\` before building.

Select a character's tools in Edit Character. Enabled tools run without asking for approval, and tool use isn't shown in the chat. Your model has to support tool calling. If the server rejects tools, Skylarping retries the reply without them.

`Skylarping\ToolDefaults.ini` sets defaults for tool options, in the same format as SimpleLLMChat's `LLMSettings.ini`. Tools > AI Services settings override this file, and this file overrides the tool's own defaults. In any option value, `{character_folder}` becomes the folder of the character using the tool. Skylarping uses this to give each character a separate place to store tool-related information, such as memories.

For more information on these tools, see [SimpleLLMChat](https://github.com/randomNinja64/SimpleLLMChat) and [SimpleLLMChat-Tool-SDK](https://github.com/randomNinja64/SimpleLLMChat-Tool-SDK).

## Credits

Thank you to the following for all of their contributions:

- [The Skymu Team](https://github.com/TheSkymuTeam/Skymu) for Skymu, which Skylarping is forked from
- OmegaAOL for the Yggdrasil API, which the chat backend interface is adapted from
- [LoRd_MuldeR](https://github.com/lordmulder/cURL-build-win32) for the cURL builds for XP
- Daniel Pirch and The WebRTC project authors for [libfvad](https://github.com/dpirch/libfvad)
- Alexandre Mutel for [Markdig](https://github.com/xoofx/markdig)
- Mark Heath and contributors for [NAudio](https://github.com/naudio/NAudio)
- James Newton-King for [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)
- Xiph.Org Foundation and contributors for [SpeexDSP](https://github.com/xiph/speexdsp)
- The SQLite Development Team for [System.Data.SQLite](https://system.data.sqlite.org)
- Daniel Stenberg and contributors for [cURL](https://github.com/curl/curl)

## Licensing

This project's code is licensed under the AGPL v3, the same as Skymu. See [LICENSE.md](LICENSE.md).

The files adapted from the Yggdrasil API (`Skylarping/Models/Models.cs`, `Skylarping/Models/ModelEnumerations.cs`, `Skylarping/Backend/IChatBackend.cs`, `Skylarping/Backend/BackendEvents.cs`) are licensed under Apache-2.0.

Third-party software included with the app is licensed as follows: libfvad (BSD-3-Clause + [WebRTC patent grant](ThirdParty/libfvad/PATENTS)), Markdig (BSD-2-Clause), NAudio (MIT), Newtonsoft.Json (MIT), SpeexDSP (BSD-3-Clause) and System.Data.SQLite (public domain). libfvad, NAudio and SpeexDSP are kept as source in [`ThirdParty/`](ThirdParty).

cURL (MIT-style license) is optional and isn't included in this repository. Licensing information is available in the THIRD_PARTY_LICENSES folder installed alongside the application.

The tests use NUnit and NUnit3TestAdapter (MIT) and Microsoft.NET.Test.Sdk (proprietary), which aren't shipped with the app.