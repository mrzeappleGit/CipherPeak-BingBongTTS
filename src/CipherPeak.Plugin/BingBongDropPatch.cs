using System;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Puts a dropped Bing Bong back into the world as one of the mod's own.
    ///
    /// This is the hole that made the world fill up with Bing Bongs. Stowing an item destroys its
    /// world object and dropping it builds a fresh one, and the fresh one carries none of the
    /// instantiation data the director recognises. So a Bing Bong you picked up and put down again
    /// left the pool for good, the director counted itself one short, and spawned another - every
    /// single time.
    ///
    /// Loosening adoption to "any Bing Bong counts" fixed the count and broke something worse: the
    /// director adopted the ones the level had placed and sent chat's voice to a Bing Bong halfway
    /// across the mountain. Fixing it here instead keeps adoption strict.
    ///
    /// Only the master client instantiates in this RPC, so only the master is intercepted; everyone
    /// else runs the vanilla body, which just empties the slot.
    /// </summary>
    [HarmonyPatch(typeof(CharacterItems), "DropItemFromSlotRPC")]
    internal static class BingBongDropPatch
    {
        // CharacterAfflictions.UpdateWeight is internal to the game assembly.
        private static readonly System.Reflection.MethodInfo UpdateWeight =
            AccessTools.Method(typeof(CharacterAfflictions), "UpdateWeight");

        private static bool Prefix(byte slotID, Vector3 spawnPosition, Character ___character)
        {
            if (!PhotonNetwork.IsMasterClient) return true;

            var world = UnityBingBongWorld.Current;
            if (world == null || ___character == null || ___character.player == null) return true;

            var slot = ___character.player.GetItemSlot(slotID);
            if (slot == null || slot.IsEmpty()) return true;
            if (!BingBongSlot.IsBingBong(slot.prefab)) return true;

            try
            {
                if (world.SpawnAt(spawnPosition) == 0) return true;   // could not spawn; let vanilla drop it

                ___character.player.EmptySlot(Optionable<byte>.Some(slotID));
                if (UpdateWeight != null) UpdateWeight.Invoke(___character.refs.afflictions, null);

                BingBongSlot.Trace("dropped Bing Bong re-spawned as a mod one so it stays in the pool.");
                return false;
            }
            catch (Exception ex)
            {
                if (BingBongSlot.Log != null)
                    BingBongSlot.Log.Warn("Could not re-spawn a dropped Bing Bong: " + ex.Message);
                return true;
            }
        }
    }
}
