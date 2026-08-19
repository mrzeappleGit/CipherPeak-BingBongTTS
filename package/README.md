# Bing Bong TTS

Reads eligible Twitch chat aloud through **two mod-controlled Bing Bongs**, in 3D positional audio,
synchronised across the whole lobby.

- Two Bing Bongs per run. Ones that are picked up or destroyed are replaced, never duplicated, and ones you leave behind stay where you left them.
- Messages alternate between them, one at a time, never overlapping.
- Carrying one does not silence it: a Bing Bong in your hands or your pocket speaks through you.
- Only the host connects to Twitch. Clients just need the mod installed — no keys, no config.
- ElevenLabs TTS. Voice aliases keep provider voice ids out of your chat.
- The Bing Bong gets its own inventory slot, so carrying one costs you neither a slot nor weight.
- Optional profanity filter: bleep the swear and speak the rest, or drop the message.

## Setup (host)

1. Install this mod and launch PEAK once so the config is generated.
2. Edit `BepInEx/config/com.cipherpeak.bingbongtts.cfg`:
   - `Twitch.Channel` — your channel name, no `#`. (Leave `Username`/`OAuthToken` empty for
     anonymous read-only chat.)
   - `TTS.ElevenLabsApiKey` — your key, or set `CIPHERPEAK_ELEVENLABS_API_KEY` instead.
   - `TTS.VoiceAliases` — `alias=elevenlabs:voiceId`, comma-separated.
3. Start a run.

Everyone else in the lobby only needs the mod installed.

## Controls

| | Chat (broadcaster/mods) | Hotkey |
|---|---|---|
| Skip current message | `!tts skip` | `F7` |
| Clear the queue | `!tts clear` | `F8` |
| Enable / disable | `!tts on` / `!tts off` | `F9` |
| Pick a voice | `!voice <alias>` | — |
| Equip / stow the Bing Bong slot | — | `5` |
| Open your own backpack | — | `B` |

## The Bing Bong slot

`BingBong/DedicatedSlot` (on by default) gives the Bing Bong an inventory slot of its own instead of
spending one of your three. The slot sits outside the game's carry-weight sum, so what is in it never
weighs anything. It has its own HUD widget and its own key, because the game's scroll cycle only
walks the vanilla slots.

Everyone who wants the slot needs the mod. Players without it are detected over the network and
left on the vanilla path, so hosting for unmodded friends is safe — they just get an ordinary slot.
`BingBong/Weightless` covers the other case: a Bing Bong carried in a normal slot, weighing nothing.

## Profanity filter

`Filter/Profanity` is `Off`, `Mask` (replace each swear with `Filter/ProfanityMask`, default "beep",
and speak the rest) or `Block` (drop the message). A built-in stem list covers the common ones and
their suffixed forms while leaving ordinary words alone; `Filter/ProfanityWords` extends it.

## Volume

`Escape` → Settings → Audio has a **Bing Bong TTS Volume** slider, on top of the game's SFX slider.
Moving it writes straight back to the config file.

## Configurable

Message length limits, queue size, per-user and global cooldowns, blocked words, ignored users,
link blocking, subscriber/moderator gating, voice aliases, volume and 3D falloff, and optional
distance rules for replacing Bing Bongs left behind. All in the `.cfg`, no recompile needed.

## TikTok

TikTok publishes no documented, permitted TTS API. The TikTok provider exists behind the same
interface but ships **disabled** and will not scrape — it only activates if you supply your own
endpoint. With it selected and unconfigured, the mod logs the reason and falls back to ElevenLabs.

Full docs, troubleshooting and architecture notes are in the source repository.
