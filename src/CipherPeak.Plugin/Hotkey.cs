using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Hotkey test for a game you play with your hands on the keyboard.
    ///
    /// <c>KeyboardShortcut.IsDown()</c> cannot be used here: its modifier check ends with
    /// <c>_modifierBlockKeyCodes.All(c =&gt; !GetKey(c) || allKeys.Contains(c))</c>, which means the
    /// shortcut only fires when no other keyboard key is held at all. That is right for a config
    /// menu and wrong for PEAK - walking holds W, sprinting holds shift, and the hotkey silently
    /// does nothing the entire time. Hence: main key down, declared modifiers held, everything else
    /// ignored.
    ///
    /// Deliberately not an extension method named IsDown: the instance method would win overload
    /// resolution and the fix would quietly never run.
    ///
    /// Reads through BepInEx's UnityInput rather than UnityEngine.Input - same source
    /// KeyboardShortcut itself uses, and it saves referencing InputLegacyModule.
    /// </summary>
    internal static class Hotkey
    {
        internal static bool Pressed(KeyboardShortcut shortcut)
        {
            var main = shortcut.MainKey;
            if (main == KeyCode.None) return false;
            if (!UnityInput.Current.GetKeyDown(main)) return false;

            foreach (var modifier in shortcut.Modifiers)
                if (!UnityInput.Current.GetKey(modifier)) return false;

            return true;
        }

        /// <summary>Main key still down. Modifiers are not required to stay held.</summary>
        internal static bool Held(KeyboardShortcut shortcut)
        {
            var main = shortcut.MainKey;
            return main != KeyCode.None && UnityInput.Current.GetKey(main);
        }

        /// <summary>Short label for a HUD slot: Alpha5 reads as "5", Keypad5 as "Num5".</summary>
        internal static string Label(KeyboardShortcut shortcut)
        {
            var main = shortcut.MainKey;
            if (main == KeyCode.None) return "";

            string name = main.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) return name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) return "Num" + name.Substring(6);
            return name;
        }
    }
}
