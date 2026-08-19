using System;
using System.Collections.Generic;
using CipherPeak.Core.BingBong;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Photon/Unity implementation of <see cref="IBingBongWorld"/>.
    ///
    /// Mod-controlled Bing Bongs are spawned as Photon *room objects*, which only the master client
    /// can create or destroy - that is what makes "host only" structural here rather than a policy
    /// check. They carry a marker in their instantiation data purely as provenance for a log; no
    /// decision reads it, because a Bing Bong that a scout dropped is just as good a speaker as one
    /// this mod spawned, and telling them apart is what used to leave extras lying around.
    /// </summary>
    internal sealed class UnityBingBongWorld : IBingBongWorld
    {
        private const string Marker = "CipherPeak.BingBong.v1";

        private readonly Func<ModSettings> _settings;
        private readonly Func<bool> _inPlayableRun;
        private readonly Func<int, bool> _isSpeaking;
        private readonly ILog _log;

        private string _prefabName;
        private bool _prefabErrorLogged;

        /// <summary>View ids this mod spawned. Only these may be destroyed - see <see cref="Despawn"/>.</summary>
        private readonly HashSet<int> _ours = new HashSet<int>();

        public UnityBingBongWorld(
            Func<ModSettings> settings,
            Func<bool> inPlayableRun,
            Func<int, bool> isSpeaking,
            ILog log)
        {
            _settings = settings;
            _inPlayableRun = inPlayableRun;
            _isSpeaking = isSpeaking;
            _log = log;
            Current = this;
        }

        public bool CanManage =>
            PhotonNetwork.InRoom
            && PhotonNetwork.IsMasterClient
            && _inPlayableRun()
            && ResolvePrefabName() != null;

        /// <summary>
        /// Every Bing Bong standing in the world that nobody is carrying, whether this mod spawned it
        /// or a scout dropped it.
        ///
        /// Only Bing Bongs this mod created, identified by the marker in their instantiation data.
        /// Adopting every Bing Bong in the world instead looks tempting - it makes dropped ones stop
        /// counting as lost - but it also adopts the ones the level placed, and then speech comes out
        /// of a Bing Bong somewhere across the mountain, which sounds exactly like the mod being
        /// broken. <see cref="BingBongDropPatch"/> closes the dropped-one hole at the source instead.
        ///
        /// Held ones are excluded because <see cref="CarriedCount"/> already counts them from the slot
        /// they were equipped from; counting both would have the director believe in twice as many
        /// Bing Bongs as exist.
        /// </summary>
        public IReadOnlyList<int> FindManaged()
        {
            var found = new List<int>(2);

            foreach (var view in PhotonNetwork.PhotonViewCollection)
            {
                if (view == null || view.gameObject == null) continue;
                if (!IsMarked(view)) continue;

                var item = view.GetComponent<Item>();
                if (item != null && item.holderCharacter != null) continue;   // counted via its carrier

                found.Add(view.ViewID);
            }

            // A scout carrying one speaks it themselves. At most one handle per scout, however many
            // they are carrying: a character has a single view to play a voice out of.
            var characters = PlayerHandler.GetAllPlayerCharacters();
            if (characters != null)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    var character = characters[i];
                    if (character == null || character.player == null) continue;
                    if (CarriedBy(character.player) == 0) continue;

                    int viewId = ViewIdOf(character);
                    if (viewId != 0 && !found.Contains(viewId)) found.Add(viewId);
                }
            }

            return found;
        }

        /// <summary>
        /// Whether a voice from this handle would actually reach anyone. A Bing Bong left behind is
        /// still a perfectly good Bing Bong, but speaking through one 53 m away with a 60 m falloff
        /// is indistinguishable from the mod being broken - so the rotation skips it while a closer
        /// one exists.
        /// </summary>
        internal bool CanBeHeard(int handle)
        {
            var view = PhotonView.Find(handle);
            if (view == null || view.gameObject == null) return false;

            Vector3 position = PositionOf(view);

            Character nearest;
            float distance;
            if (!TryFindNearestCharacter(position, out nearest, out distance)) return true;

            return distance <= _settings().Audio.MaxDistance;
        }

        /// <summary>A Character's root transform does not follow its ragdoll; its torso does.</summary>
        private static Vector3 PositionOf(PhotonView view)
        {
            var character = view.GetComponent<Character>();
            return character != null ? character.Center : view.transform.position;
        }

        /// <summary>Human-readable "where is this voice coming from", for the log.</summary>
        internal string Describe(int handle)
        {
            var view = PhotonView.Find(handle);
            if (view == null || view.gameObject == null) return "view " + handle + " (missing)";

            string what = view.GetComponent<Character>() != null
                ? "scout " + view.gameObject.name
                : "Bing Bong " + handle;

            var local = Character.localCharacter;
            if (local == null) return what;

            float distance = Vector3.Distance(PositionOf(view), local.Center);
            return what + ", " + distance.ToString("F0") + "m away";
        }

        private static int ViewIdOf(Character character)
        {
            var view = character.GetComponent<PhotonView>();
            return view == null ? 0 : view.ViewID;
        }

        private static bool IsMarked(PhotonView view)
        {
            var data = view.InstantiationData;
            return data != null && data.Length > 0 && (data[0] as string) == Marker;
        }

        /// <summary>The live instance, so the drop patch can put a marked Bing Bong back in the world.</summary>
        internal static UnityBingBongWorld Current;

        public int Spawn()
        {
            Vector3 position;
            if (!TryFindSpawnPosition(out position)) return 0;
            return SpawnAt(position);
        }

        /// <summary>Spawns a mod-marked Bing Bong at an exact spot.</summary>
        internal int SpawnAt(Vector3 position)
        {
            string prefab = ResolvePrefabName();
            if (prefab == null) return 0;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return 0;

            GameObject spawned;
            try
            {
                spawned = PhotonNetwork.InstantiateRoomObject(
                    "0_Items/" + prefab,
                    position,
                    UnityEngine.Random.rotation,
                    0,
                    new object[] { Marker });
            }
            catch (Exception ex)
            {
                _log.Error("Failed to spawn Bing Bong: " + ex.Message);
                return 0;
            }

            if (spawned == null) return 0;

            var view = spawned.GetComponent<PhotonView>();
            if (view == null)
            {
                _log.Error("Spawned Bing Bong has no PhotonView; destroying it again.");
                PhotonNetwork.Destroy(spawned);
                return 0;
            }

            _ours.Add(view.ViewID);
            return view.ViewID;
        }

        /// <summary>
        /// Destroys a Bing Bong this mod spawned. One a scout dropped is only forgotten, never
        /// destroyed: the director's "too far from the party" rule exists to stop its own Bing Bongs
        /// being stranded, and applying it to someone else's means teleporting away a Bing Bong they
        /// deliberately put down. Forgetting is enough - the director then tops the world back up.
        /// </summary>
        public void Despawn(int handle)
        {
            if (!_ours.Remove(handle)) return;                      // not ours to destroy

            var view = PhotonView.Find(handle);
            if (view == null || view.gameObject == null) return;   // already gone
            if (!PhotonNetwork.IsMasterClient) return;             // only the owner may destroy room objects

            try { PhotonNetwork.Destroy(view.gameObject); }
            catch (Exception ex) { _log.Warn("Failed to despawn Bing Bong " + handle + ": " + ex.Message); }
        }

        /// <summary>
        /// How many Bing Bongs this scout has on them, across every slot. Read from the slots rather
        /// than tracked as pickup events: a stowed item has no world object to follow, and a count
        /// that is simply read cannot drift out of sync with reality.
        /// </summary>
        private int CarriedBy(Player player)
        {
            int count = 0;

            var slots = player.itemSlots;
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                    if (IsBingBongIn(slots[i])) count++;

            if (IsBingBongIn(player.tempFullSlot)) count++;
            if (_settings().BingBong.DedicatedSlot && IsBingBongIn(BingBongSlot.For(player))) count++;

            count += InBackpack(player);
            return count;
        }

        /// <summary>Backpack contents hang off the backpack slot's instance data, not off the player.</summary>
        private static int InBackpack(Player player)
        {
            var backpack = player.backpackSlot;
            if (backpack == null || backpack.IsEmpty() || backpack.data == null) return 0;

            BackpackData data;
            if (!backpack.data.TryGetDataEntry(DataEntryKey.BackpackData, out data)) return 0;
            if (data == null || data.itemSlots == null) return 0;

            int count = 0;
            for (int i = 0; i < data.itemSlots.Length; i++)
                if (IsBingBongIn(data.itemSlots[i])) count++;

            return count;
        }

        private static bool IsBingBongIn(ItemSlot slot) =>
            slot != null && !slot.IsEmpty() && BingBongSlot.IsBingBong(slot.prefab);

        public bool IsAlive(int handle)
        {
            var view = PhotonView.Find(handle);
            if (view == null || view.gameObject == null || !view.gameObject.activeInHierarchy) return false;

            // Never yank a Bing Bong out from under its own sentence.
            if (_isSpeaking(handle)) return true;

            // A scout-as-speaker lives exactly as long as they are still carrying one.
            var character = view.GetComponent<Character>();
            if (character != null)
                return character.player != null && CarriedBy(character.player) > 0;

            var item = view.GetComponent<Item>();
            if (item != null && item.holderCharacter != null) return true;   // a scout is carrying it

            var bingBong = _settings().BingBong;

            // Both limits off by default: a Bing Bong you walked away from is where you left it, not
            // lost, and replacing it means one silently appearing beside you every time the party
            // gains height. Set either to a positive number to have it replaced again.
            bool checkDistance = bingBong.MaxDistanceFromPlayersMeters > 0;
            bool checkDrop = bingBong.OutOfBoundsDropMeters > 0;
            if (!checkDistance && !checkDrop) return true;

            Vector3 position = view.transform.position;

            Character nearest;
            float distance;
            if (!TryFindNearestCharacter(position, out nearest, out distance)) return true; // nobody to measure against yet

            if (checkDistance && distance > bingBong.MaxDistanceFromPlayersMeters) return false;
            if (checkDrop && nearest.Center.y - position.y > bingBong.OutOfBoundsDropMeters) return false;

            return true;
        }

        private string ResolvePrefabName()
        {
            if (_prefabName != null) return _prefabName;

            string configured = _settings().BingBong.PrefabNameOverride;
            if (!string.IsNullOrEmpty(configured))
            {
                _prefabName = configured;
                return _prefabName;
            }

            ItemDatabase database;
            try { database = SingletonAsset<ItemDatabase>.Instance; }
            catch (Exception ex)
            {
                _log.Warn("Item database not ready yet (" + ex.Message + "); retrying later.");
                return null;
            }

            if (database == null || database.itemLookup == null || database.itemLookup.Count == 0) return null;

            foreach (var entry in database.itemLookup)
            {
                var item = entry.Value;
                if (item == null) continue;

                // The Bing Bong item is the one carrying the "ask" action; matching on the component
                // rather than the name survives renames and localisation.
                if (item.GetComponentInChildren<Action_AskBingBong>(true) == null) continue;

                _prefabName = item.gameObject.name;
                _log.Info("Resolved Bing Bong prefab as '0_Items/" + _prefabName + "'.");
                return _prefabName;
            }

            // Not latched: the database can still be filling in. Log once, keep retrying.
            if (!_prefabErrorLogged)
            {
                _prefabErrorLogged = true;
                _log.Error("Could not find a Bing Bong item in the item database. " +
                           "If this persists, set BingBong.PrefabNameOverride (normally 'BingBong').");
            }
            return null;
        }

        /// <summary>
        /// Picks a grounded, unobstructed point near the party, falling back to this seat's spawn
        /// point before the local character exists.
        /// </summary>
        private bool TryFindSpawnPosition(out Vector3 position)
        {
            var settings = _settings().BingBong;
            position = Vector3.zero;

            Vector3 anchor;
            if (!TryFindAnchor(out anchor)) return false;

            float radius = Mathf.Max(1f, (float)settings.SpawnRadiusMeters);
            float height = Mathf.Max(0.5f, (float)settings.SpawnHeightOffsetMeters);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized
                                 * UnityEngine.Random.Range(1f, radius);
                Vector3 probe = anchor + new Vector3(offset.x, height, offset.y);

                RaycastHit hit = HelperFunctions.LineCheck(
                    probe, probe + Vector3.down * (height + 8f),
                    HelperFunctions.LayerType.AllPhysicalExceptCharacter);

                if (hit.transform == null) continue;

                Vector3 candidate = hit.point + Vector3.up * 0.5f;
                if (Physics.CheckSphere(candidate, 0.35f,
                        HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore))
                    continue;

                position = candidate;
                return true;
            }

            // No clean ground found: drop it just above the anchor and let physics settle it.
            position = anchor + Vector3.up * height;
            return true;
        }

        private static bool TryFindAnchor(out Vector3 anchor)
        {
            var local = Character.localCharacter;
            if (local != null)
            {
                anchor = local.Center;
                return true;
            }

            var characters = Character.AllCharacters;
            if (characters != null)
            {
                for (int i = 0; i < characters.Count; i++)
                    if (characters[i] != null && !characters[i].isBot)
                    {
                        anchor = characters[i].Center;
                        return true;
                    }
            }

            var spawnPoints = SpawnPoint.allSpawnPoints;
            if (spawnPoints != null && spawnPoints.Count > 0 && spawnPoints[0] != null)
            {
                anchor = spawnPoints[0].transform.position;
                return true;
            }

            anchor = Vector3.zero;
            return false;
        }

        private static bool TryFindNearestCharacter(Vector3 from, out Character nearest, out float distance)
        {
            nearest = null;
            distance = float.MaxValue;

            var characters = Character.AllCharacters;
            if (characters == null) return false;

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                if (character == null || character.isBot) continue;

                float d = Vector3.Distance(from, character.Center);
                if (d >= distance) continue;
                distance = d;
                nearest = character;
            }

            return nearest != null;
        }
    }
}
