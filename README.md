![Skylarping Logo](Images/logo.png) 

# What is Skylarping?
Skylarping is an AI character chat app that looks and feels like classic Skype 4, and runs on Windows XP and later (.NET Framework 4.0). It is a fork of [Skymu](https://github.com/TheSkymuTeam/Skymu) by The Skymu Team, and is licensed under the AGPL v3 like its upstream.

![Client, chatting](Images/skymu-v0.4-chat.png) 

![Client, calling](Images/skymu-v0.4-call.png) 

# Character cards
Skylarping imports character cards as PNG, JSON or CHARX, exports them as PNG or JSON (right-click a contact), and supports SillyTavern's macros, such as `{{user}}`, `{{time}}` and `{{pick::a::b}}`. See [docs/Macros.md](docs/Macros.md) for each macro and what it does.

# Group chats
Group chats hold several characters at once. Characters reply when you name them or tag them with `@`, and otherwise one or two of them answer, weighted by each character's talkativeness. They can also reply to each other, and talk on their own when the chat goes quiet. Each group has its own settings for this: right-click it and choose Edit Group.

Group calls work the same way, with each character speaking in their own voice, one at a time. A group call starts quietly, and only you can interrupt a character.

# Build Guide
For this guide, you will need Git for Windows, Visual Studio 2019 (or newer) and NSIS 3 (or newer). Support will not be provided if older build tools are used. 

* [Download Git for Windows](https://git-scm.com/install/windows) and install. For the default editor, we recommend you select Notepad (or Notepad++, etc. if you have it) instead of Vim, unless of course you feel like dedicating a few days to learning how to use Vim. Stick with the default options for the other things the installer asks you.
* [Download Visual Studio 2019](https://aka.ms/vs/16/release/vs_community.exe) (recommended) or [download Visual Studio 2026](https://aka.ms/vs/18/Stable/vs_community.exe). Select the ".NET desktop development" workload in the installer.
* [Download NSIS](https://sourceforge.net/projects/nsis/files/latest/download)

## Application

Open Command Prompt, navigate to the folder you want Skymu in, and run `git clone https://github.com/TheSkymuTeam/Skymu.git` to download the repository.

<img width="559" height="190" alt="image" src="https://github.com/user-attachments/assets/6544bcbe-45e5-4348-ba60-dab1d31abab8" />

Open the `Skylarping.sln` solution file with Visual Studio.

<img width="227" height="139" alt="image" src="https://github.com/user-attachments/assets/84e4b169-8adc-47c8-8926-f64f9269ff28" />
 
Select the "Debug" configuration in the action bar.

<img width="323" height="142" alt="image" src="https://github.com/user-attachments/assets/13bf677b-70be-46e7-bdc6-4138d19c6272" />

If you want to change the internal name (recommended to avoid conflicts with your existing Skylarping installation!) or version, go to `Skylarping/App.xaml.cs` in the Solution Explorer pane, click to open the file, and then edit these values. The internal name also sets the data folder (`%AppData%\<name>`).

<img width="555" height="335" alt="image" src="https://github.com/user-attachments/assets/af6029f8-20c7-40c3-bada-0b2bf98ee1e7" />

Press `Ctrl + Shift + B` to build the solution for the first time, or click "Build Solution" in the menu bar under "Build". Skylarping targets .NET Framework 4.0 only (Windows XP and later); the build output is in `Skylarping/bin/Debug` or `Skylarping/bin/Release`.

<img width="403" height="183" alt="image" src="https://github.com/user-attachments/assets/19b70367-21a5-4388-81a5-a521818d4fba" />
 
Click the play button on the action bar to run your copy of Skylarping. 

<img width="184" height="78" alt="image" src="https://github.com/user-attachments/assets/b46f5c8b-08c5-4e18-b3cd-05c7585122b2" />

If you are encountering conflicts after making your changes, click "Rebuild Solution" to clean cache and build from scratch.

<img width="351" height="96" alt="image" src="https://github.com/user-attachments/assets/a3dc5ce0-c6aa-4454-ac6d-b04f790d3335" />

## Installer
Select the "Release" configuration in the action bar and build Skylarping in Visual Studio. The installer packages everything in `Skylarping/bin/Release`; for HTTPS on Windows XP, copy `curl.exe` and `curl-ca-bundle.crt` into that folder before compiling the installer.

<img width="207" height="137" alt="image" src="https://github.com/user-attachments/assets/c42e4a0c-9341-4e9c-900c-1c07a18df647" />

Navigate to the `NSIS` folder, right-click on `SkylarpingSetup.nsi`, and then click "Compile NSIS Script".

<img width="362" height="135" alt="image" src="https://github.com/user-attachments/assets/82f484dc-3a03-4b9b-abc0-1c56d9b1bfca" />

The NSIS compiler will run. After it's finished, the output log will turn green and `Skylarping Installer.exe` will show up in the folder; click "Test Installer" to run it.

<img width="636" height="540" alt="image" src="https://github.com/user-attachments/assets/07ef2931-fb03-4df7-acbe-c854999221fd" />

# Open-source software used

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
Everything below runs on .NET Framework 4.0 (Windows XP and later). Libraries marked *vendored* are kept as source in [`ThirdParty/`](ThirdParty), each with a README giving the exact upstream version, archive hash and any changes; the app build copies the licenses of vendored code to `THIRD_PARTY_LICENSES\`.

### Shipped with the app

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| Yggdrasil API, adapted (`Skylarping/Models/Models.cs`, `Skylarping/Models/ModelEnumerations.cs`, `Skylarping/Backend/IChatBackend.cs`, `Skylarping/Backend/BackendEvents.cs`) | OmegaAOL | [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) | Skylarping (chat backend interface, models and events) | [Skylarping/Models](Skylarping/Models), [Skylarping/Backend](Skylarping/Backend) |
| libfvad 1.0 (vendored) | Daniel Pirch, The WebRTC project authors | [BSD-3-Clause](https://choosealicense.com/licenses/bsd-3-clause/) + [WebRTC patent grant](ThirdParty/libfvad/PATENTS) | Skylarping (voice activity detection for calls) | [Repo](https://github.com/dpirch/libfvad), [ThirdParty/libfvad](ThirdParty/libfvad) |
| Markdig 0.18.3 | Alexandre Mutel | [BSD-2-Clause](https://choosealicense.com/licenses/bsd-2-clause/) | Skylarping (message formatting) | [Repo](https://github.com/xoofx/markdig) |
| NAudio 2.2.1, Core + WinMM (vendored) | Mark Heath and contributors | [MIT](https://choosealicense.com/licenses/mit/) | Skylarping (sounds, Stub backend, voice calls) | [Repo](https://github.com/naudio/NAudio), [ThirdParty/naudio](ThirdParty/naudio) |
| Newtonsoft.Json 13.0.3 | James Newton-King | [MIT](https://choosealicense.com/licenses/mit/) | Skylarping | [Repo](https://github.com/JamesNK/Newtonsoft.Json) |
| SpeexDSP 1.2.1 (vendored) | Xiph.Org Foundation and contributors | [BSD-3-Clause](https://choosealicense.com/licenses/bsd-3-clause/) | Skylarping (echo cancellation for calls) | [Repo](https://github.com/xiph/speexdsp), [ThirdParty/speexdsp](ThirdParty/speexdsp) |
| System.Data.SQLite 1.0.119 (includes SQLite) | SQLite Development Team | [Public domain](https://www.sqlite.org/copyright.html) | Skylarping (local database) | [Website](https://system.data.sqlite.org) |

### Optional, not included in this repository

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| curl (`curl.exe` + `curl-ca-bundle.crt`, placed next to the app) | Daniel Stenberg and contributors | [curl license](https://curl.se/docs/copyright.html) (MIT-style) | Skylarping (`Skylarping/Net`): HTTPS fallback where Windows' own TLS is too old (Windows XP) | [Repo](https://github.com/curl/curl) |

### Build and test only (not shipped)

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| NUnit 3.13.3 | Charlie Poole, Rob Prouse | [MIT](https://choosealicense.com/licenses/mit/) | Skylarping.Tests | [Repo](https://github.com/nunit/nunit) |
| NUnit3TestAdapter 3.17.0 | Charlie Poole, Terje Sandstrom | [MIT](https://choosealicense.com/licenses/mit/) | Skylarping.Tests | [Repo](https://github.com/nunit/nunit3-vs-adapter) |
| Microsoft.NET.Test.Sdk 16.11.0 | Microsoft | Microsoft .NET Library license (proprietary; test tooling only, never distributed) | Skylarping.Tests | [Repo](https://github.com/microsoft/vstest) |

Some .NET 4.0 support code is written for this project because the usual packages either have no .NET 4.0 build or aren't AGPL-compatible: `async`/`await` support (`Skylarping/Compat/AsyncSupport.cs`, replacing Microsoft.Bcl.Async, whose license conflicts with the AGPL), tuples (`Skylarping/Compat/ValueTuple.cs`), MVVM helpers (`Skylarping/Mvvm/`, replacing CommunityToolkit.Mvvm) and the custom window frame (`Skylarping/Classes/Windows/WindowChrome.cs`). The .NET Framework and its WPF theme assemblies are system components and are not redistributed.
