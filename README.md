![Skymu Logo](Images/logo.png) 

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/TheSkymuTeam/Skymu/total)
![GitHub contributors](https://img.shields.io/github/contributors-anon/TheSkymuTeam/Skymu)
![GitHub Created At](https://img.shields.io/github/created-at/TheSkymuTeam/Skymu)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/TheSkymuTeam/Skymu)
![GitHub watchers](https://img.shields.io/github/watchers/TheSkymuTeam/Skymu)
![GitHub repo stars](https://img.shields.io/github/stars/TheSkymuTeam/Skymu)

# What is Skymu?
Skymu is a modern multi-platform IM client that looks like classic versions of Skype, with skins perfectly resembling Skype 4, 5, 6, and 7. Officially supported messaging services include Matrix, Tox, MSNP11, and some other platforms.

![Client, chatting](Images/skymu-v0.4-chat.png) 

![Client, calling](Images/skymu-v0.4-call.png) 

# Build Guide
For this guide, you will need Git for Windows, Visual Studio 2019 (or newer) and NSIS 3 (or newer). Support will not be provided if older build tools are used. 

* [Download Git for Windows](https://git-scm.com/install/windows) and install. For the default editor, we recommend you select Notepad (or Notepad++, etc. if you have it) instead of Vim, unless of course you feel like dedicating a few days to learning how to use Vim. Stick with the default options for the other things the installer asks you.
* [Download Visual Studio 2019](https://aka.ms/vs/16/release/vs_community.exe) (recommended) or [download Visual Studio 2026](https://aka.ms/vs/18/Stable/vs_community.exe). Select the ".NET desktop development" workload in the installer.
* [Download NSIS](https://sourceforge.net/projects/nsis/files/latest/download)

## Application

Open Command Prompt, navigate to the folder you want Skymu in, and run `git clone https://github.com/TheSkymuTeam/Skymu.git` to download the repository.

<img width="559" height="190" alt="image" src="https://github.com/user-attachments/assets/6544bcbe-45e5-4348-ba60-dab1d31abab8" />

Open the `Skymu.sln` solution file with Visual Studio.

<img width="227" height="139" alt="image" src="https://github.com/user-attachments/assets/84e4b169-8adc-47c8-8926-f64f9269ff28" />
 
Select the "Debug" configuration in the action bar.

<img width="323" height="142" alt="image" src="https://github.com/user-attachments/assets/13bf677b-70be-46e7-bdc6-4138d19c6272" />

If you want to change the internal name (recommended to avoid conflicts with your existing Skymu installation!) or version, go to `Skymu/Skymu/App.xaml.cs` in the Solution Explorer pane, click to open the file, and then edit these values. Note that changing this will make the updater stop working.

<img width="555" height="335" alt="image" src="https://github.com/user-attachments/assets/af6029f8-20c7-40c3-bada-0b2bf98ee1e7" />

Press `Ctrl + Shift + B` to build the solution for the first time, or click "Build Solution" in the menu bar under "Build". *Notice for advanced users:* Both .NET Core and .NET Framework builds of Skymu are built at the same time and can be found in `/Skymu/bin/Skymu.Core` and `/Skymu/bin/Skymu.Legacy` respectively.

<img width="403" height="183" alt="image" src="https://github.com/user-attachments/assets/19b70367-21a5-4388-81a5-a521818d4fba" />
 
Click the play button on the action bar to run your copy of Skymu. 

<img width="184" height="78" alt="image" src="https://github.com/user-attachments/assets/b46f5c8b-08c5-4e18-b3cd-05c7585122b2" />

If you are encountering conflicts after making your changes, click "Rebuild Solution" to clean cache and build from scratch.

<img width="351" height="96" alt="image" src="https://github.com/user-attachments/assets/a3dc5ce0-c6aa-4454-ac6d-b04f790d3335" />

## Installer
Select the "Release" configuration in the action bar and build Skymu in Visual Studio.

<img width="207" height="137" alt="image" src="https://github.com/user-attachments/assets/c42e4a0c-9341-4e9c-900c-1c07a18df647" />

Navigate to the `NSIS` folder, right-click on the type of installer you want to build, and then click "Compile NSIS Script". `SkymuSetup.nsi` is for the standard installer, `SkymuBetaSetup.nsi` is for the beta installer, both are functionally the same and differ only in appearance.

<img width="362" height="135" alt="image" src="https://github.com/user-attachments/assets/82f484dc-3a03-4b9b-abc0-1c56d9b1bfca" />

The NSIS compiler will run. After it's finished, the output log will turn green and `Skymu Installer.exe` will show up in the folder; click "Test Installer" to run it.

<img width="636" height="540" alt="image" src="https://github.com/user-attachments/assets/07ef2931-fb03-4df7-acbe-c854999221fd" />

# Open-source software used

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
Everything below runs on .NET Framework 4.0 (Windows XP and later). Libraries marked *vendored* are kept as source in [`ThirdParty/`](ThirdParty), each with a README giving the exact upstream version, archive hash and any changes; the app build copies the licenses of vendored code to `THIRD_PARTY_LICENSES\`.

### Shipped with the app

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| Yggdrasil API, adapted (`Skymu/Models/Models.cs`, `Skymu/Models/ModelEnumerations.cs`, `Skymu/Backend/IChatBackend.cs`, `Skymu/Backend/BackendEvents.cs`) | OmegaAOL | [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) | Skymu (chat backend interface, models and events) | [Skymu/Models](Skymu/Models), [Skymu/Backend](Skymu/Backend) |
| libfvad 1.0 (vendored) | Daniel Pirch, The WebRTC project authors | [BSD-3-Clause](https://choosealicense.com/licenses/bsd-3-clause/) + [WebRTC patent grant](ThirdParty/libfvad/PATENTS) | Skymu (voice activity detection for calls) | [Repo](https://github.com/dpirch/libfvad), [ThirdParty/libfvad](ThirdParty/libfvad) |
| Markdig 0.18.3 | Alexandre Mutel | [BSD-2-Clause](https://choosealicense.com/licenses/bsd-2-clause/) | Skymu (message formatting) | [Repo](https://github.com/xoofx/markdig) |
| NAudio 2.2.1, Core + WinMM (vendored) | Mark Heath and contributors | [MIT](https://choosealicense.com/licenses/mit/) | Skymu (sounds, Stub backend, voice calls) | [Repo](https://github.com/naudio/NAudio), [ThirdParty/naudio](ThirdParty/naudio) |
| Newtonsoft.Json 13.0.3 | James Newton-King | [MIT](https://choosealicense.com/licenses/mit/) | Skymu | [Repo](https://github.com/JamesNK/Newtonsoft.Json) |
| SpeexDSP 1.2.1 (vendored) | Xiph.Org Foundation and contributors | [BSD-3-Clause](https://choosealicense.com/licenses/bsd-3-clause/) | Skymu (echo cancellation for calls) | [Repo](https://github.com/xiph/speexdsp), [ThirdParty/speexdsp](ThirdParty/speexdsp) |
| System.Data.SQLite 1.0.119 (includes SQLite) | SQLite Development Team | [Public domain](https://www.sqlite.org/copyright.html) | Skymu (local database) | [Website](https://system.data.sqlite.org) |

### Optional, not included in this repository

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| curl (`curl.exe` + `curl-ca-bundle.crt`, placed next to the app) | Daniel Stenberg and contributors | [curl license](https://curl.se/docs/copyright.html) (MIT-style) | Skymu (`Skymu/Net`): HTTPS fallback where Windows' own TLS is too old (Windows XP) | [Repo](https://github.com/curl/curl) |

### Build and test only (not shipped)

| Software | Author | License | Used in | Source code |
|---|---|---|---|---|
| NUnit 3.13.3 | Charlie Poole, Rob Prouse | [MIT](https://choosealicense.com/licenses/mit/) | Skymu.Tests | [Repo](https://github.com/nunit/nunit) |
| NUnit3TestAdapter 3.17.0 | Charlie Poole, Terje Sandstrom | [MIT](https://choosealicense.com/licenses/mit/) | Skymu.Tests | [Repo](https://github.com/nunit/nunit3-vs-adapter) |
| Microsoft.NET.Test.Sdk 16.11.0 | Microsoft | Microsoft .NET Library license (proprietary; test tooling only, never distributed) | Skymu.Tests | [Repo](https://github.com/microsoft/vstest) |

Some .NET 4.0 support code is written for this project because the usual packages either have no .NET 4.0 build or aren't AGPL-compatible: `async`/`await` support (`Skymu/Compat/AsyncSupport.cs`, replacing Microsoft.Bcl.Async, whose license conflicts with the AGPL), tuples (`Skymu/Compat/ValueTuple.cs`), MVVM helpers (`Skymu/Mvvm/`, replacing CommunityToolkit.Mvvm) and the custom window frame (`Skymu/Classes/Windows/WindowChrome.cs`). The .NET Framework and its WPF theme assemblies are system components and are not redistributed.
