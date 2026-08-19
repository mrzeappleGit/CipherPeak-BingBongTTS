# Changelog

## 1.0.0

- Two host-managed Bing Bongs spawned at the start of every playable run, replaced automatically
  when picked up or destroyed. Ones left behind stay put; `BingBong/MaxDistanceFromPlayersMeters`
  and `BingBong/OutOfBoundsDropMeters` turn distance-based replacement back on.
- Twitch chat ingestion (host only) with filtering, per-user and global cooldowns, and a bounded queue.
- ElevenLabs text-to-speech; TikTok provider present behind the same interface but disabled unless
  the operator supplies their own endpoint (TikTok publishes no permitted public TTS API).
- 3D positional playback from alternating Bing Bongs, synchronised so every player hears each
  message exactly once and messages never overlap. A Bing Bong in a scout's hands or pocket
  speaks through that scout, so carrying one does not silence it.
- `BingBong/DedicatedSlot` (on by default): the Bing Bong gets its own inventory slot instead of
  spending one of the normal three, with its own HUD widget and its own hotkey (`Alpha5`). The slot
  sits outside the game's weight sum, so what is in it never weighs anything. Each player who wants
  it needs the mod. Players without it are detected and left on the vanilla path, so a mixed
  lobby behaves normally for them instead of breaking their pickups.
- `BingBong/Weightless`: carry a Bing Bong without it counting toward your carry weight in any slot.
  Off by default, and it applies to your own character, so each player who wants it needs the mod.
- TTS volume is a slider in the game's own options screen, under Audio. It writes back to the
  config file, so the `.cfg` stays the one place the value is persisted.
- `B` opens the wheel for the backpack on your own back, no need to drop it first. It is the
  game's own wheel, opened from a key; rocketpacks are skipped so the key never lights one.
- `Filter/Profanity`: Off, Mask (bleep the swear, speak the rest) or Block (drop the message).
  Extend the built-in stem list with `Filter/ProfanityWords`; change the bleep with `Filter/ProfanityMask`.
- `!voice`, `!tts skip|clear|on|off` chat commands and F7 / F8 / F9 hotkeys.
