# CipherPeak — Bing Bong TTS

A BepInEx mod for **PEAK** that reads eligible Twitch chat messages aloud through **two
mod-controlled Bing Bongs**, in 3D positional audio, synchronised across the whole lobby.

- Exactly two mod-owned Bing Bongs exist in every playable run. Lost ones are replaced, never duplicated.
- Messages alternate between the two, one at a time, never overlapping.
- Only the host talks to Twitch and drives the queue. Clients receive the finished audio.
- ElevenLabs is the supported TTS provider. TikTok is present behind the same interface but ships
  disabled — see [TikTok limitation](#tiktok-limitation).

---

## Requirements

| | |
|---|---|
| Game | PEAK (verified against build **2.2.a**, Steam app id `3527290`) |
| Loader | [BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/) (BepInEx 5.4.23.x) |
| TTS | An ElevenLabs account and API key |
| Twitch | Nothing, for read-only anonymous chat. Optionally an OAuth token with `chat:read` |

---

## Installation

### With a mod manager (recommended)

1. Install **BepInExPack PEAK** from Thunderstore.
2. Install **CipherPeak_BingBongTTS**.
3. Launch PEAK once through the manager so the config file is generated.
4. Edit `BepInEx/config/com.cipherpeak.bingbongtts.cfg` (see [Configuration](#configuration)).

### Manual

1. Extract BepInExPack PEAK into the PEAK folder (next to `PEAK.exe`), then launch the game once so
   `BepInEx/` is created.
2. Copy `CipherPeak.BingBongTTS.dll` **and** `CipherPeak.Core.dll` into
   `PEAK/BepInEx/plugins/CipherPeak-BingBongTTS/`.
3. Launch the game once, then edit `PEAK/BepInEx/config/com.cipherpeak.bingbongtts.cfg`.

`config/com.cipherpeak.bingbongtts.cfg.example` in this repository shows every setting with
placeholder credentials.

> **Only the host needs the mod configured.** Other players still need the mod installed to hear
> anything (they receive the audio over the network), but they need no keys and no Twitch settings.

---

## Building from source

```sh
git clone <this repo>
cd CipherPeak
./build.ps1                     # builds Release, runs tests, writes dist/CipherPeak_BingBongTTS-1.0.0.zip
```

The plugin compiles against the game's own assemblies. The build probes the usual Steam library
paths; if yours is elsewhere:

```sh
./build.ps1 -PeakDir "D:\SteamLibrary\steamapps\common\PEAK"
# or
$env:PEAK_DIR = "D:\SteamLibrary\steamapps\common\PEAK"
```

To iterate quickly, `./build.ps1 -PeakDir "..." -Deploy` copies the DLLs straight into
`BepInEx/plugins`.

Core and the test project build with no game installed:

```sh
dotnet test tests/CipherPeak.Tests/CipherPeak.Tests.csproj
```

`NuGet.config` includes the BepInEx feed (`https://nuget.bepinex.dev/v3/index.json`) because
`BepInEx.Core` is not published on nuget.org.

---

## Authentication

### Twitch

Two options.

**Anonymous (default, no credentials).** Leave `Twitch.Username` and `Twitch.OAuthToken` empty. The
mod logs in as `justinfan<random>`, which is read-only. This form is not in Twitch's official
documentation, but it is the long-standing way to read chat without credentials and needs no account.

**Authenticated (documented path).** Generate a token with the `chat:read` scope — the easiest route
is [twitchtokengenerator.com](https://twitchtokengenerator.com/) or your own app via the
[Twitch OAuth docs](https://dev.twitch.tv/docs/authentication/). Then set:

```ini
Username   = your_lowercase_login
OAuthToken = oauth:xxxxxxxxxxxxxxxxxxxxxxxxxx     # the 'oauth:' prefix is optional
```

The mod never writes to chat, so `chat:edit` is not needed.

### ElevenLabs

Create a key at <https://elevenlabs.io/app/settings/api-keys> and copy a voice id from
<https://elevenlabs.io/app/voice-library>. Then either:

```ini
[TTS]
ElevenLabsApiKey = sk_...
VoiceAliases     = default=elevenlabs:21m00Tcm4TlvDq8ikWAM
```

…or keep the key out of the file entirely:

```sh
setx CIPHERPEAK_ELEVENLABS_API_KEY "sk_..."
```

### Keeping credentials out of source control

- `.gitignore` excludes `config/*.cfg`; only the `.example` is tracked.
- Environment variables always win over the config file:
  `CIPHERPEAK_ELEVENLABS_API_KEY`, `CIPHERPEAK_TWITCH_OAUTH`, `CIPHERPEAK_TIKTOK_SESSION_ID`.
- Every configured secret is registered with a scrubber that replaces it with `<redacted>` in all
  log output, so a stray exception message cannot leak a key.

---

## Configuration

Full annotated settings live in the generated `.cfg` and in
`config/com.cipherpeak.bingbongtts.cfg.example`. The ones people actually change:

| Setting | Default | Notes |
|---|---|---|
| `Twitch.Channel` | *(empty)* | Required. Without it nothing connects. |
| `Filter.MaxMessageLength` | `200` | Longer messages are dropped, not truncated. |
| `Filter.BlockedWords` | *(empty)* | Comma-separated substrings, case-insensitive. |
| `Filter.SubscribersOnly` / `ModeratorsOnly` | `false` | Permission gates. |
| `Queue.PerUserCooldownSeconds` | `20` | Per-chatter rate limit. |
| `Queue.MaxQueuedMessages` | `10` | Backlog cap. |
| `TTS.VoiceAliases` | one entry | `alias=provider:voiceId`, comma-separated. |
| `TTS.AllowVoiceCommand` | `false` | Enables `!voice <alias>` for chatters. |
| `BingBong.MaxDistanceFromPlayersMeters` | `40` | Beyond this a Bing Bong is treated as lost and respawned near the party. Raise it if you would rather they stay put. |
| `Audio.Volume` / `MinDistance` / `MaxDistance` | `1 / 3 / 60` | On top of the game's SFX slider. |

Nothing here needs a recompile. Edits take effect on the next game start, or immediately if you use
[BepInEx ConfigurationManager](https://thunderstore.io/c/peak/p/BepInEx/BepInExConfigurationManager/).

### Voice aliases

Chat never sees a provider voice id. You define aliases:

```ini
VoiceAliases = default=elevenlabs:21m00Tcm4TlvDq8ikWAM, gravel=elevenlabs:pNInz6obpgDQGcFmaJgB
```

and chatters (when `AllowVoiceCommand = true`) pick one with `!voice gravel`. Anything not in the
list is refused. Announce the alias names yourself — the mod is read-only and cannot reply in chat.

### Controls

| | Chat (broadcaster/mods) | Hotkey |
|---|---|---|
| Skip current message | `!tts skip` | `F7` |
| Clear the queue | `!tts clear` | `F8` |
| Enable / disable TTS | `!tts on` / `!tts off` | `F9` toggles |
| Pick a voice (any chatter) | `!voice <alias>` | — |

---

## How it behaves in a run

- Two Bing Bongs are spawned near the party as soon as a playable scene, map and local character exist.
- They are ordinary Bing Bong items — you can pick them up, throw them, and carry them. A carried
  Bing Bong is never replaced.
- If one is destroyed, falls far below the party, or is left more than
  `MaxDistanceFromPlayersMeters` behind, it is removed and a fresh one appears near the party.
- While one is missing, the remaining Bing Bong takes every message until the replacement arrives.
- Leaving the run, returning to the airport, or disabling the mod removes both.

---

## Troubleshooting

**Nothing is spoken and the log says `Twitch channel is not set`.**
`Twitch.Channel` is empty. Set it to the channel name without the `#`.

**`Twitch chat connected` appears but nothing is ever queued.**
Check the BepInEx console for `Dropped message from …: <verdict>`. Common causes: `BlockLinks`
catching a bare domain, `PerUserCooldownSeconds` still running, `SubscribersOnly` on, or the message
starting with one of `CommandPrefixes`.

**`Twitch TLS certificate could not be validated`.**
Unity's Mono runtime usually ships without a root certificate store. The mod connects anyway and
logs this once. Set `Twitch.AllowInsecureTls = false` if you would rather it refuse.

**`Twitch rejected the login credentials` / auth failures.**
The token expired or lacks `chat:read`, or `Username` does not match the token's account. The
reconnect loop deliberately stops after an auth failure instead of hammering Twitch — fix the token
and toggle the mod off/on (`F9`) or restart the game.

**`ElevenLabs HTTP 401 (bad or missing API key)`.**
Key is wrong or empty. Remember the environment variable overrides the config file — if
`CIPHERPEAK_ELEVENLABS_API_KEY` is set to something stale, it wins.

**`ElevenLabs HTTP 404 (voice id not found)`.**
The voice id in `VoiceAliases` is not on your account. Copy it again from the ElevenLabs voice library.

**`No Bing Bong available to speak`.**
Both are mid-replacement, or the run is not in a spawnable state yet. It resolves within one
`LifecycleTickSeconds`. If it persists, check for
`Could not find a Bing Bong item in the item database` and set `BingBong.PrefabNameOverride = BingBong`.

**Bing Bongs respawn constantly while climbing.**
That is `MaxDistanceFromPlayersMeters` doing its job. Raise it (e.g. `150`) to let them stay where
they were dropped — at the cost of eventually being out of earshot.

**Only the host hears anything.**
The other players do not have the mod installed. They need the DLLs; they do not need any config.

**Nothing at all in the log.**
Check `BepInEx/LogOutput.log` for `CipherPeak Bing Bong TTS 1.0.0 loaded.` If it is missing, BepInEx
did not load the plugin — confirm both DLLs are in `BepInEx/plugins/` and that BepInEx itself started.

---

## Limitations

### TikTok limitation

**TikTok publishes no documented, permitted text-to-speech API.** Every "TikTok TTS" library in the
wild calls a private mobile endpoint with a borrowed session cookie: undocumented, rate-limited
without notice, blocked at TikTok's discretion, and against TikTok's terms of service.

This mod therefore **does not ship a TikTok endpoint**. `TikTokTtsProvider` implements the same
`ITtsProvider` interface as ElevenLabs, but it reports itself unavailable — with an explicit reason
in the log — unless *you* set both `TTS.TikTokEndpoint` and `TTS.TikTokSessionId` to something you
are allowed to use (for example your own proxy). It never discovers, scrapes, or hardcodes a host.

With TikTok selected and unconfigured, the router logs the reason and falls back to ElevenLabs
(when `FallbackToOtherProvider = true`), rather than failing silently or breaking when an endpoint
disappears.

### Other known limitations

- **Read-only chat.** The mod never sends messages, so it cannot confirm a `!voice` change in chat
  or list aliases. Results go to the BepInEx log only.
- **Config reload.** Edits to the `.cfg` apply on the next game start, or live with BepInEx
  ConfigurationManager. There is no in-game reload command.
- **Clients need the mod.** Audio is distributed over Photon to mod-aware clients; vanilla players
  in the lobby hear nothing and see two extra Bing Bongs.
- **Late joiners** hear messages from the moment they join. Nothing is replayed — by design.
- **Message size.** Clips are capped by `TTS.MaxAudioBytes` (512 KB) and chunked at 12 KB over
  Photon. Very long messages are rejected by `Filter.MaxMessageLength` well before that.
- **Mouth animation** reuses the game's `BingBongMouth` rig; if a game update changes it, the mouth
  stops moving but speech is unaffected.
- **Not verified against Photon Cloud rate limits at scale.** See
  [Verification](#verification) for exactly what was and was not tested.

---

## Verification

| | |
|---|---|
| `dotnet build CipherPeak.sln -c Release` | ✅ succeeds, 0 warnings, 0 errors |
| `dotnet test` | ✅ **127 tests, 0 failures** |
| Game API surface | ✅ every PEAK/Photon symbol used was read out of `Assembly-CSharp.dll` and `PhotonUnityNetworking.dll` from the local install (build 2.2.a), not assumed |
| ElevenLabs request shape | ✅ checked against the current [convert endpoint docs](https://elevenlabs.io/docs/api-reference/text-to-speech/convert) and asserted in tests |
| Twitch IRC login sequence | ✅ checked against the current [Twitch IRC docs](https://dev.twitch.tv/docs/chat/irc/) |
| TikTok API | ✅ confirmed **no** public permitted API exists; provider ships disabled |
| **In-game runtime behaviour** | ⚠️ **not executed.** PEAK was not launched, so spawning, playback, and multiplayer sync are verified by construction and unit tests, not by play-testing. |

Test coverage by requirement:

| Requirement | Tests |
|---|---|
| Filtering | `MessageFilterTests` (17) — empty, commands, links, blocked words, length, duplicates, sub/mod gating, bypasses |
| Cooldowns | `SpeechQueueTests` — per-user, per-user isolation, global, rejection does not start a cooldown |
| Queue ordering | `SpeechQueueTests` — FIFO, ids, capacity, clear vs reset |
| Configuration | `VoiceAliasTests`, `VoiceRegistryTests`, `DefaultSettingsTests` — parsing, allowlisting, safe defaults |
| Provider failures | `ElevenLabsProviderTests`, `TikTokProviderTests`, `TtsRouterTests` — 401/404/429/500, timeouts, JSON-as-audio, oversized bodies, malformed base64, a provider that throws, retries, fallback, caching |
| "Exactly two Bing Bongs" | `BingBongDirectorTests` (12) — first tick, idempotent ticks, replacement, adoption on reconnect, surplus removal, spawn failure retry, host migration, scene reload |
| Multiplayer audio transport | `AudioChunkProtocolTests` — round trip, out-of-order, duplicates, interleaving, eviction |
| Chat commands & ingestion | `ChatPipelineTests` — permissions, voice command gating, disabled state |

Run them yourself with `dotnet test tests/CipherPeak.Tests/CipherPeak.Tests.csproj`.

---

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Licence

MIT.
