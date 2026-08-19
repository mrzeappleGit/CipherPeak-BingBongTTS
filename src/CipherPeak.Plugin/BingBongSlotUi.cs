using System;
using HarmonyLib;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Draws the Bing Bong slot on the HUD.
    ///
    /// The widget is a clone of a normal hotbar slot, with <c>isTemporarySlot</c> forced on.
    ///
    /// Not a clone of <c>GUIManager.temporaryItem</c>, which is the obvious candidate and is wrong:
    /// that widget is "the item in your hands" and its RawImage ships with hand art baked in from
    /// the scene (<c>defaultIcon</c> and <c>carryingIcon</c> are serialized fields the code never
    /// assigns), so the clone showed a hand instead of the Bing Bong. A normal slot draws whatever
    /// item it is given. The <c>isTemporarySlot</c> flag is kept because it forces the "selected"
    /// styling in <c>SetSelected</c>, which is what makes an out-of-band slot render lit rather than
    /// comparing its sibling index against a slot id it can never match.
    ///
    /// Nothing here rebuilds <c>GUIManager.items</c>: that array is indexed against
    /// <c>Player.itemSlots</c>, and appending to it would walk straight into the same id collision
    /// the slot itself was designed around.
    /// </summary>
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdateItems))]
    internal static class BingBongSlotUi
    {
        private static InventoryItemUI _widget;
        private static bool _failed;
        private static Item _lastDrawn;

        private static void Postfix(GUIManager __instance)
        {
            if (_failed) return;

            var settings = BingBongSlot.Settings();
            if (settings == null) return;

            if (!settings.BingBong.DedicatedSlot)
            {
                if (_widget != null) _widget.gameObject.SetActive(false);
                return;
            }

            var observed = Character.observedCharacter;
            if (observed == null || observed.player == null) return;

            var slot = BingBongSlot.For(observed.player);
            if (slot == null) return;

            var widget = Widget(__instance, (float)settings.BingBong.SlotUiOffsetX);
            if (widget == null) return;

            if (slot.IsEmpty())
            {
                if (widget.gameObject.activeSelf) widget.gameObject.SetActive(false);
                return;
            }

            if (!widget.gameObject.activeSelf) widget.gameObject.SetActive(true);
            widget.SetItem(slot);

            if (_lastDrawn != slot.prefab)
            {
                _lastDrawn = slot.prefab;
                BingBongSlot.Trace("widget drawing '" + slot.GetPrefabName() + "'.");
            }
        }

        private static InventoryItemUI Widget(GUIManager gui, float offsetX)
        {
            if (_widget != null) return _widget;

            // A normal hotbar slot, falling back to the temporary slot only if the hotbar is missing.
            var template = gui.items != null && gui.items.Length > 0 && gui.items[gui.items.Length - 1] != null
                ? gui.items[gui.items.Length - 1]
                : gui.temporaryItem;
            if (template == null) return null;

            try
            {
                var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                clone.name = "CipherPeak.BingBongSlot";

                _widget = clone.GetComponent<InventoryItemUI>();
                if (_widget == null)
                {
                    UnityEngine.Object.Destroy(clone);
                    _failed = true;
                    return null;
                }

                // Out-of-band slot: it can never match a sibling index, so light it like the temp slot.
                _widget.isTemporarySlot = true;
                _widget.isBackpack = false;

                Relabel(clone, _widget.nameText);

                Place(clone, template.gameObject, offsetX);
                clone.SetActive(false);

                var rect = clone.GetComponent<RectTransform>();
                var templateRect = template.GetComponent<RectTransform>();
                BingBongSlot.Trace(string.Format(
                    "widget cloned from '{0}' under '{1}' (layout group: {2}); template at {3} size {4}, widget at {5}",
                    template.name,
                    template.transform.parent == null ? "<none>" : template.transform.parent.name,
                    HasLayoutGroup(template.transform.parent),
                    templateRect == null ? "?" : templateRect.anchoredPosition.ToString(),
                    templateRect == null ? "?" : templateRect.rect.size.ToString(),
                    rect == null ? "?" : rect.anchoredPosition.ToString()));

                return _widget;
            }
            catch (Exception ex)
            {
                _failed = true;   // a broken HUD is worse than a missing widget; never retry per-frame
                if (BingBongSlot.Log != null)
                    BingBongSlot.Log.Warn("Could not create the Bing Bong slot widget: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// If the parent lays its children out, stay out of the way. Otherwise the clone would sit
        /// exactly on top of the temporary slot, so step it sideways by its own width.
        /// ponytail: SlotUiOffsetX overrides this - the scene is not inspectable from a decompiler,
        /// so the knob is there for whatever the layout actually turns out to be.
        /// </summary>
        private static void Place(GameObject clone, GameObject template, float offsetX)
        {
            var rect = clone.GetComponent<RectTransform>();
            if (rect == null) return;

            if (Math.Abs(offsetX) > 0.01f)
            {
                rect.anchoredPosition += new Vector2(offsetX, 0f);
                return;
            }

            if (HasLayoutGroup(template.transform.parent)) return;

            var templateRect = template.GetComponent<RectTransform>();
            float width = templateRect == null ? 90f : templateRect.rect.width;
            rect.anchoredPosition += new Vector2(-(width * 1.1f), 0f);
        }

        /// <summary>
        /// The slot number under a hotbar slot is static text baked into the scene - no field on
        /// InventoryItemUI drives it - so a clone of slot three keeps saying "3". Rewrite the numeric
        /// label to whatever key is actually bound, skipping the item-name text.
        /// </summary>
        /// <summary>
        /// Puts the bound key on the widget.
        ///
        /// The number on a hotbar slot is not text: it is a TMP sprite tag - <c>&lt;sprite=3 tint=1&gt;</c> -
        /// pointing into a keyboard glyph atlas, written by an <c>InputIcon</c> component from an
        /// <c>InputSpriteData.InputAction</c>. Setting the string is pointless while that component
        /// lives, because it rewrites it on every enable and every input-device change, and there is
        /// no InputAction for a key the game does not have. So the component goes, and the label
        /// becomes plain text. It loses the glyph styling and gains being correct for any binding.
        /// </summary>
        private static void Relabel(GameObject clone, TMPro.TMP_Text nameText)
        {
            string label = BingBongSlot.KeyLabel();
            if (string.IsNullOrEmpty(label)) return;

            foreach (var icon in clone.GetComponentsInChildren<InputIcon>(true))
            {
                if (icon == null) continue;

                var text = icon.GetComponent<TMPro.TMP_Text>();
                UnityEngine.Object.DestroyImmediate(icon);   // before it can rewrite the tag again

                if (text == null || text == nameText) continue;

                text.text = label;
                text.enabled = true;
                BingBongSlot.Trace("relabelled the slot key icon to '" + label + "'.");
            }
        }

        private static bool HasLayoutGroup(Transform parent) =>
            parent != null && parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

        internal static void Reset()
        {
            if (_widget != null) UnityEngine.Object.Destroy(_widget.gameObject);
            _widget = null;
            _lastDrawn = null;
            _failed = false;
        }
    }
}
