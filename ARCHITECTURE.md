# Architecture

## The one decision everything else follows from

**`CipherPeak.Core` contains no Unity, BepInEx or Photon reference.** Filtering, queueing,
cooldowns, voice aliasing, TTS providers, the audio chunk protocol, speaker alternation and the
"exactly two Bing Bongs" rule are all plain C# behind narrow interfaces. That is what makes the
required test suite possible at all — none of it needs a running game.

`CipherPeak.Plugin` is the thin adapter: it maps BepInEx config into a settings snapshot, implements
the two ports Core defines, and owns everything that must touch the game thread.

```
                        ┌──────────────────── HOST ONLY ────────────────────┐
Twitch IRC ──▶ TwitchIrcClient ──▶ ChatPipeline ──▶ SpeechQueue ──▶ SpeakPump
 (TLS, own thread)          │            │                              │
                            │            ├── ChatCommandProcessor       │
                            │            ├── MessageFilter              ▼
                            │            └── VoiceRegistry          TtsRouter ──▶ ElevenLabs
                            │                                           │         TikTok (opt-in)
                            │                                           ▼
                            │                                     FileAudioCache
                            │                                           │
                        BingBongDirector ◀── UnityBingBongWorld         │
                        (exactly two)         (Photon room objects)     │
                            │                                           ▼
                            └──── speaker view id ───────────▶  NetworkAudioBus
                                                                        │
                        └───────────────────────────────────────────────┼──────┘
                                                                        │ Photon event 177
                                            ┌───────────────────────────┴───────────────┐
                                            ▼                                           ▼
                                    ALL CLIENTS: AudioReassembler ──▶ TtsPlaybackManager
                                                                     (one clip at a time,
                                                                      AudioSource on the Bing Bong)
```

## Modules

| Module | Assembly | Responsibility |
|---|---|---|
| `Twitch/TwitchIrcClient` | Core | TLS IRC socket on its own thread, backoff reconnect, bounded inbox |
| `Twitch/IrcMessageParser` | Core | Pure IRCv3 line → `ChatMessage` |
| `Filtering/MessageFilter` | Core | Eligibility: empty, commands, bots, links, blocked words, length, duplicates, permissions |
| `Queueing/SpeechQueue` | Core | FIFO admission with per-user and global cooldowns and a capacity cap |
| `Commands/ChatCommandProcessor` | Core | `!voice`, `!tts skip\|clear\|on\|off` |
| `Ingestion/ChatPipeline` | Core | The single place chat becomes (or does not become) a queued utterance |
| `Tts/*` | Core | `ITtsProvider` (ElevenLabs, TikTok), `TtsRouter` (retries, fallback, rate-limit cooldowns), `VoiceRegistry` (alias allowlist), `FileAudioCache` |
| `BingBong/BingBongDirector` | Core | The "exactly two" state machine, over the `IBingBongWorld` port |
| `Net/AudioChunkProtocol` | Core | Split/reassemble clips for transport |
| `Speech/SpeakerRotation` | Core | Alternation with survivor fallback |
| `Net/IHttpTransport` | Core | The only outbound-HTTP seam |
| `UnityBingBongWorld` | Plugin | `IBingBongWorld` on Photon room objects |
| `NetworkAudioBus` | Plugin | Photon `RaiseEvent` fan-out and receive |
| `TtsPlaybackManager` | Plugin | Local sequential playback on a 3D `AudioSource` |
| `PluginConfig` | Plugin | BepInEx `.cfg` → `ModSettings` snapshot |
| `CipherPeakRunner` | Plugin | Lifecycle, host detection, the speak pump |

## The invariants, and what enforces each

### Exactly two mod-controlled Bing Bongs

`BingBongDirector.Tick()` runs one reconcile every `LifecycleTickSeconds`:

1. **Adopt** every mod-tagged entity that exists but is not tracked.
2. **Drop** tracked handles that are no longer alive (destroyed, out of bounds, left behind).
3. **Trim** anything past two.
4. **Top up** to two.

Adoption before spawning is the whole trick. Reconnects, scene reloads, host migration and a double
`Tick()` all funnel through the same step, so there is no path that spawns a third one. If spawning
is not possible yet (no map, no character), `Spawn()` returns 0 and the next tick retries.

### Host only

Mod Bing Bongs are spawned with `PhotonNetwork.InstantiateRoomObject`, which **PUN itself refuses
for non-master clients**. Host-only is structural, not a policy check. `CanManage` additionally
requires `PhotonNetwork.InRoom`, master-client status, a playable run, and a resolved prefab.

When a client becomes host mid-run it adopts the existing entities via their marker instead of
spawning new ones; the outgoing host calls `Forget()` (not `ReleaseAll()`) so nothing is destroyed
during the handover.

### Distinguishing mod Bing Bongs from natural ones

The marker `"CipherPeak.BingBong.v1"` is passed as PUN instantiation data. It replicates to every
client and is unforgeable by the level generator, so `FindManaged()` can never pick up a Bing Bong
the map placed.

### Positional audio, never global

`TtsPlaybackManager` creates a child `GameObject` named `CipherPeakTtsSource` **on the Bing Bong's
own transform**, with `spatialBlend = 1` and logarithmic rolloff, routed through
`SFX_Player.instance.defaultMixerGroup` so the game's SFX slider applies. There is no code path that
plays TTS anywhere else.

### Alternation, and one voice at a time

`SpeakerRotation` picks the Bing Bong that did not speak last, skipping unavailable ones. The
sequential guarantee is enforced **twice**, deliberately:

- **Host side:** the pump only synthesizes the next message once local playback of the previous one
  has finished, plus `GapBetweenMessagesSeconds`.
- **Client side:** `TtsPlaybackManager` has a single "currently playing" slot and a local queue, so
  even with network jitter two clips cannot overlap on any machine.

### Every client hears each message exactly once

The host synthesizes once and broadcasts to `ReceiverGroup.All` — which includes itself. The host is
not a special case; it runs the identical receive-and-play path as every other client. No message is
buffered, so late joiners get everything from the moment they join and nothing before it.

Clips are chunked at 12 KB and paced four chunks per frame under Photon event code `177` (PEAK's own
custom commands use codes below 6; Photon reserves 200+). `AudioReassembler` tolerates out-of-order
and duplicate chunks and evicts abandoned partial messages so a dropped tail cannot leak memory.

### Failure containment

- Provider calls go through `IHttpTransport`; every failure mode returns a `TtsResult` rather than
  throwing. `TtsRouter` also wraps each provider in try/catch, so even a provider that throws
  becomes a logged skipped message.
- 429s set a per-provider cooldown honoured by later calls.
- The Twitch thread never touches Unity objects; it hands `ChatMessage` over a
  `ConcurrentQueue` that the game thread drains with a per-frame budget.
- An auth failure stops the reconnect loop instead of hammering Twitch.
- Secrets are registered with `SecretScrubber` and replaced with `<redacted>` in all log output.

## Deliberate simplifications

- **No Harmony patches.** Polling `GameHandler.IsInGameplayScene` / `MapHandler.Exists` /
  `Character.localCharacter` covers the whole lifecycle, and PUN callbacks cover the rest. Fewer
  places to break on a game update.
- **`MaxDistanceFromPlayersMeters` doubles as "the Bing Bongs keep up with you".** They are not
  given follow behaviour; a Bing Bong left too far behind is simply treated as lost and replaced
  near the party. Same code, no new mechanic — at the cost of respawn churn if the value is low.
- **Read-only Twitch connection.** No `chat:edit` scope, no outbound messages, so command results
  go to the log rather than to chat.
- **Hand-rolled JSON.** Two small `IndexOf`-based readers and one string builder, instead of taking
  a dependency for three fields.
