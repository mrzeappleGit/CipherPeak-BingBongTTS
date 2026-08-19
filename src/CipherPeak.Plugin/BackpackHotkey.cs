using System;
using HarmonyLib;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Opens the backpack wheel for the pack on your own back, without taking it off first.
    ///
    /// This is the same call the game makes when someone else interacts with your backpack
    /// (<c>BackpackOnBackVisuals.Interact_CastFinished</c>): the wheel does not care whether the pack
    /// is on the ground or on a scout, only that it gets a BackpackReference. So there is no new UI
    /// here and nothing is reimplemented - it is the vanilla wheel, opened from a key.
    ///
    /// A rocketpack is deliberately skipped: vanilla's interact path lights the rocket rather than
    /// opening a wheel, and a hotkey that fires a rocket by surprise is not a convenience.
    /// </summary>
    internal static class BackpackHotkey
    {
        /// <summary>True while the configured backpack key is held. Set from the plugin.</summary>
        internal static Func<bool> KeyHeld = () => false;

        internal static void Open()
        {
            var character = Character.localCharacter;
            if (character == null || character.player == null) return;

            var data = character.data;
            if (data == null || !data.fullyConscious || data.dead) return;

            // The wheel steals the cursor; opening it mid-climb would strand the scout.
            if (data.isClimbing || data.isRopeClimbing || data.usingBackpackWheel) return;

            var slot = character.player.backpackSlot;
            if (slot == null || slot.IsEmpty())
            {
                BingBongSlot.Trace("backpack key ignored: nothing on your back.");
                return;
            }

            if (slot.backpackType == BackpackSlot.BackpackType.Rocketpack)
            {
                BingBongSlot.Trace("backpack key ignored: a rocketpack has no wheel.");
                return;
            }

            try
            {
                var visuals = character.refs.backpackHandler.activeBackpackVisuals;
                if (visuals == null) return;

                var gui = GUIManager.instance;
                if (gui == null) return;

                gui.OpenBackpackWheel(
                    BackpackReference.GetFromEquippedBackpack(character),
                    visuals.slotCount,
                    visuals.backpackType);
            }
            catch (Exception ex)
            {
                if (BingBongSlot.Log != null)
                    BingBongSlot.Log.Warn("Could not open the backpack wheel: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Keeps the wheel open while the backpack key is held.
    ///
    /// The wheel is a hold-to-browse radial menu: its Update closes on the first frame where
    /// <c>input.interactIsPressed</c> is false, and that same branch calls <c>Choose()</c>. Opening it
    /// from another key therefore closed it again immediately.
    ///
    /// So the key stands in for interact, but only inside this one method: the prefix raises the flag
    /// and the postfix puts it straight back. Suppressing the close instead would leave <c>Choose()</c>
    /// running every frame, taking an item over and over. Release the key and vanilla's own branch
    /// chooses and closes, which is exactly the interact-key feel.
    /// </summary>
    [HarmonyPatch(typeof(BackpackWheel), "Update")]
    internal static class BackpackWheelHoldPatch
    {
        private static bool _spoofed;

        private static void Prefix()
        {
            _spoofed = false;

            var character = Character.localCharacter;
            if (character == null || character.input == null) return;
            if (character.input.interactIsPressed) return;      // really held; nothing to do
            if (!BackpackHotkey.KeyHeld()) return;

            character.input.interactIsPressed = true;
            _spoofed = true;
        }

        private static void Postfix()
        {
            if (!_spoofed) return;
            _spoofed = false;

            var character = Character.localCharacter;
            if (character != null && character.input != null)
                character.input.interactIsPressed = false;
        }
    }
}
