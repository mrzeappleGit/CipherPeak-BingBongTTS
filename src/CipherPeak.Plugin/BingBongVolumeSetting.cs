using System;
using HarmonyLib;
using Unity.Mathematics;
using Zorro.Settings;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// TTS volume as a slider in the game's own options screen, under Audio.
    ///
    /// The category string has to be one of the <c>SettingsCategory</c> enum names - the menu filters
    /// with <c>GetCategory() == category.ToString()</c>, so anything else would never match a tab.
    ///
    /// The slider is the live value and writes straight back to the BepInEx config, so the .cfg stays
    /// the one place the setting is persisted and r2modman's config editor keeps working. The game
    /// also saves it under its own key; on a fresh install the config value seeds the default.
    /// </summary>
    public sealed class BingBongVolumeSetting : FloatSetting, IExposedSetting
    {
        internal static Func<float> ReadConfig = () => 1f;
        internal static Action<float> WriteConfig = _ => { };

        public override void ApplyValue()
        {
            WriteConfig(Value);
        }

        protected override float GetDefaultValue() => ReadConfig();

        protected override float2 GetMinMaxValue() => new float2(0f, 1f);

        public string GetDisplayName() => "Bing Bong TTS Volume";

        public string GetCategory() => "Audio";
    }

    /// <summary>
    /// Adds the slider as the settings handler is built. The menu calls RefreshSettings on every
    /// open, so registering here is early enough for the setting to appear on the first visit.
    /// </summary>
    [HarmonyPatch(typeof(SettingsHandler), MethodType.Constructor)]
    internal static class SettingsHandlerPatch
    {
        private static void Postfix(SettingsHandler __instance)
        {
            try
            {
                __instance.AddSetting(new BingBongVolumeSetting());
                BingBongSlot.Trace("registered the TTS volume slider under Audio.");
            }
            catch (Exception ex)
            {
                if (BingBongSlot.Log != null)
                    BingBongSlot.Log.Warn("Could not add the volume slider to the options menu: " + ex.Message);
            }
        }
    }
}
