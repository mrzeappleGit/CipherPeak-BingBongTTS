using System;
using System.Runtime.CompilerServices;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// A fourth inventory slot that only ever holds a Bing Bong.
    ///
    /// Why a new slot id and not a fourth entry in <c>Player.itemSlots</c>: <c>Player.GetItemSlot</c>
    /// hard-codes <c>case 3: return backpackSlot</c> before it indexes the array, so an item at
    /// index 3 would be unreachable through the one accessor every equip, drop and pickup path uses.
    /// Id 249 sits next to the game's own out-of-band slot (the temporary slot, 250) and collides
    /// with nothing.
    ///
    /// The slot lives outside <c>InventorySyncData</c>, so it does not ride along on the vanilla
    /// inventory sync - <see cref="BingBongSlotSync"/> replicates it separately.
    /// </summary>
    internal static class BingBongSlot
    {
        internal const byte SlotId = 249;

        /// <summary>Per-player slot storage. Weak keys, so a player leaving the room takes its slot with it.</summary>
        private static readonly ConditionalWeakTable<Player, ItemSlot> Slots =
            new ConditionalWeakTable<Player, ItemSlot>();

        internal static Func<ModSettings> Settings = () => null;
        internal static ILog Log;

        /// <summary>Label for the key bound to the slot, shown on its HUD widget.</summary>
        internal static Func<string> KeyLabel = () => "";

        internal static bool Enabled
        {
            get
            {
                var settings = Settings();
                return settings != null && settings.BingBong.DedicatedSlot;
            }
        }

        internal static ItemSlot For(Player player)
        {
            if (player == null) return null;

            ItemSlot slot;
            if (Slots.TryGetValue(player, out slot)) return slot;

            slot = new ItemSlot(SlotId, player);
            Slots.Add(player, slot);
            return slot;
        }

        /// <summary>True when the prefab is the Bing Bong, tested by component rather than by name.</summary>
        internal static bool IsBingBong(Item prefab) =>
            prefab != null && prefab.GetComponentInChildren<Action_AskBingBong>(true) != null;

        internal static void Trace(string message)
        {
            if (Log != null) Log.Info("[slot] " + message);
        }

        /// <summary>Equip or unequip the slot on the local character. Bound to a hotkey.</summary>
        internal static void ToggleEquip()
        {
            var character = Character.localCharacter;
            if (character == null || character.player == null) return;

            if (!Enabled)
            {
                Trace("hotkey ignored: DedicatedSlot is off.");
                return;
            }

            var slot = For(character.player);
            if (slot == null || slot.IsEmpty())
            {
                Trace("hotkey ignored: the Bing Bong slot is empty.");
                return;
            }

            var items = character.refs.items;
            bool alreadyHolding = items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == SlotId;

            items.EquipSlot(alreadyHolding ? Optionable<byte>.None : Optionable<byte>.Some(SlotId));
        }
    }

    /// <summary>Makes slot 249 reachable through the one accessor the whole game funnels through.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetItemSlot))]
    internal static class PlayerGetItemSlotPatch
    {
        private static bool Prefix(Player __instance, byte slotID, ref ItemSlot __result)
        {
            if (slotID != BingBongSlot.SlotId) return true;

            __result = BingBongSlot.For(__instance);
            return false;
        }
    }

    /// <summary>Sends a picked-up Bing Bong to its own slot instead of spending one of the three.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.AddItem))]
    internal static class PlayerAddItemPatch
    {
        private static bool Prefix(Player __instance, ushort itemID, ItemInstanceData instanceData,
                                   ref ItemSlot slot, ref bool __result)
        {
            if (!BingBongSlot.Enabled) return true;

            // Vanilla asserts this itself; leave the error path to it.
            if (!PhotonNetwork.IsMasterClient) return true;

            Item prefab;
            if (!ItemDatabase.TryGetItem(itemID, out prefab)) return true;
            if (!BingBongSlot.IsBingBong(prefab)) return true;

            // Routing into a slot the owner's machine does not have would hand them a slot id their
            // EquipSlot cannot resolve, and it throws on the null. Vanilla behaviour for them instead.
            if (!BingBongSlotSync.HasMod(__instance))
            {
                BingBongSlot.Trace(__instance.name + " has no mod; leaving their Bing Bong in a normal slot.");
                return true;
            }

            var target = BingBongSlot.For(__instance);
            if (target == null || !target.IsEmpty())
            {
                BingBongSlot.Trace("Bing Bong picked up by " + __instance.name +
                                   " but its slot is already full; falling back to a normal slot.");
                return true;
            }

            if (instanceData == null)
            {
                instanceData = new ItemInstanceData(Guid.NewGuid());
                ItemInstanceDataHandler.AddInstanceData(instanceData);
            }

            target.SetItem(prefab, instanceData);
            slot = target;
            __result = true;

            BingBongSlot.Trace("Bing Bong routed to the dedicated slot for " + __instance.name + ".");
            BingBongSlotSync.Broadcast(__instance, target);
            return false;
        }
    }

    /// <summary>
    /// The pickup prompt is gated on <c>HasEmptySlot</c>, which only ever looks at the three normal
    /// slots and the temporary one. Without this, a full inventory would refuse a Bing Bong before
    /// <see cref="PlayerAddItemPatch"/> ever got a chance to put it in its own slot.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.HasEmptySlot))]
    internal static class PlayerHasEmptySlotPatch
    {
        private static void Postfix(Player __instance, ushort itemID, ref bool __result)
        {
            if (__result || !BingBongSlot.Enabled) return;

            Item prefab;
            if (!ItemDatabase.TryGetItem(itemID, out prefab)) return;
            if (!BingBongSlot.IsBingBong(prefab)) return;

            var slot = BingBongSlot.For(__instance);
            if (slot != null && slot.IsEmpty()) __result = true;
        }
    }

    /// <summary>
    /// Death and full-drop paths loop slots 0..3 by index, which cannot reach 249. Without this a
    /// Bing Bong would survive in a dead scout's pocket and never come back.
    /// </summary>
    // DropAllItems is internal and its Character field is private, hence the string name and the
    // ___character injection.
    [HarmonyPatch(typeof(CharacterItems), "DropAllItems")]
    internal static class DropAllItemsPatch
    {
        private static void Postfix(CharacterItems __instance, Character ___character)
        {
            if (!BingBongSlot.Enabled) return;

            var character = ___character;
            if (character == null || !character.IsLocal || character.player == null) return;

            var slot = BingBongSlot.For(character.player);
            if (slot == null || slot.IsEmpty()) return;

            Vector3 position = character.Center + Vector3.up * 0.5f;

            try
            {
                __instance.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, BingBongSlot.SlotId, position);
            }
            catch (Exception ex)
            {
                if (BingBongSlot.Log != null) BingBongSlot.Log.Warn("Could not drop the Bing Bong slot: " + ex.Message);
            }
        }
    }
}
