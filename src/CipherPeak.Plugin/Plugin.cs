using System.IO;
using BepInEx;
using CipherPeak.Core.Logging;
using HarmonyLib;
using UnityEngine;

namespace CipherPeak.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("PEAK.exe")]
    public sealed class BingBongTtsPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.cipherpeak.bingbongtts";
        public const string Name = "CipherPeak Bing Bong TTS";
        public const string Version = "1.0.0";

        private GameObject _host;
        private CipherPeakRunner _runner;
        private Harmony _harmony;

        private void Awake()
        {
            var log = new UnityLog(Logger);

            var config = new PluginConfig(Config, log);

            BingBongWeightPatch.IsEnabled = () => config.Settings.BingBong.Weightless;
            BingBongSlot.Settings = () => config.Settings;
            BingBongSlot.Log = log;
            BingBongSlot.KeyLabel = () => Hotkey.Label(config.BingBongSlotKey);
            BackpackHotkey.KeyHeld = () => Hotkey.Held(config.BackpackKey);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(BingBongWeightPatch));
            _harmony.PatchAll(typeof(PlayerGetItemSlotPatch));
            _harmony.PatchAll(typeof(PlayerAddItemPatch));
            _harmony.PatchAll(typeof(PlayerHasEmptySlotPatch));
            _harmony.PatchAll(typeof(DropAllItemsPatch));
            _harmony.PatchAll(typeof(BingBongDropPatch));
            _harmony.PatchAll(typeof(BingBongSlotUi));

            BingBongVolumeSetting.ReadConfig = () => config.Volume;
            BingBongVolumeSetting.WriteConfig = config.SetVolume;
            _harmony.PatchAll(typeof(SettingsHandlerPatch));
            _harmony.PatchAll(typeof(BackpackWheelHoldPatch));

            string root = Path.Combine(Paths.CachePath, "CipherPeak");
            string cacheDirectory = Path.Combine(root, "tts-cache");
            string playbackDirectory = Path.Combine(root, "playback");

            _host = new GameObject("CipherPeak.BingBongTTS");
            DontDestroyOnLoad(_host);

            _runner = _host.AddComponent<CipherPeakRunner>();
            _runner.Initialize(config, log, cacheDirectory, playbackDirectory);

            _host.AddComponent<BingBongSlotSync>().Initialize(log);

            log.Info(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.Shutdown();
            if (_host != null) Destroy(_host);
            if (_harmony != null) _harmony.UnpatchSelf();
            BingBongWeightPatch.Forget();
            BingBongSlotUi.Reset();
            SecretScrubber.Clear();
        }
    }
}
