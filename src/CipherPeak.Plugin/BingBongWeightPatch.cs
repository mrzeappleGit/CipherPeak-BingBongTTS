using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Makes Bing Bongs weightless while carried.
    ///
    /// Patched at <c>Item.CarryWeight</c> rather than at the callers: CharacterAfflictions.UpdateWeight
    /// sums that one property across the hotbar slots, every backpack slot, the backpack itself and the
    /// temporary slot 250. Zeroing it at the source covers all five at once, and any the game adds later.
    ///
    /// The trade this makes: UpdateWeight reads <c>itemSlot.prefab.CarryWeight</c>, i.e. the shared
    /// prefab, not the instance in your hands. A prefab cannot know which slot it sits in, so the
    /// exemption is necessarily "a Bing Bong anywhere", not "a Bing Bong in one designated slot".
    /// A slot-scoped version means reimplementing UpdateWeight wholesale.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CarryWeight), MethodType.Getter)]
    internal static class BingBongWeightPatch
    {
        /// <summary>Set by the plugin; returns the live config value.</summary>
        internal static Func<bool> IsEnabled = () => false;

        // ponytail: unbounded, but keyed by item instance id and only ever holding the item prefabs
        // the game has loaded (tens). Swap for a ConditionalWeakTable if that ever stops being true.
        private static readonly Dictionary<int, bool> IsBingBongCache = new Dictionary<int, bool>();

        private static void Postfix(Item __instance, ref int __result)
        {
            if (__result == 0 || __instance == null) return;

            bool enabled;
            try { enabled = IsEnabled(); }
            catch { return; }
            if (!enabled) return;

            if (IsBingBong(__instance)) __result = 0;
        }

        private static bool IsBingBong(Item item)
        {
            int id = item.GetInstanceID();

            bool known;
            if (IsBingBongCache.TryGetValue(id, out known)) return known;

            // Same test UnityBingBongWorld uses to find the prefab: the "ask" action is what makes
            // a Bing Bong a Bing Bong, and it survives renaming and localisation.
            known = item.GetComponentInChildren<Action_AskBingBong>(true) != null;
            IsBingBongCache[id] = known;
            return known;
        }

        internal static void Forget() => IsBingBongCache.Clear();
    }
}
