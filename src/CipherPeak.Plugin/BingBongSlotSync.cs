using System;
using System.Collections.Generic;
using CipherPeak.Core.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Replication for the Bing Bong slot.
    ///
    /// The slot is not part of <c>InventorySyncData</c>, so nothing carries it between clients on its
    /// own. Authority matches vanilla's: the master client owns inventory state and pushes it out.
    ///
    /// Rather than hooking every path that can mutate a slot - AddItem, EmptySlot, both drop RPCs,
    /// RPCRemoveItemFromSlot - this polls for change. One item id and one guid per player, twice a
    /// second, is nothing next to the certainty that no mutation path can be missed.
    /// Event code 178 sits beside the audio bus on 177: clear of PEAK's own codes and Photon's 200+.
    /// </summary>
    internal sealed class BingBongSlotSync : MonoBehaviour, IOnEventCallback
    {
        internal const byte EventCode = 178;

        /// <summary>"I have the mod." Sent by every client that does; nobody else can send it.</summary>
        internal const byte PresenceEventCode = 179;

        private const ushort Empty = ushort.MaxValue;
        private const float TickSeconds = 0.5f;
        private const float PresenceSeconds = 5f;

        private static BingBongSlotSync _instance;

        private readonly Dictionary<int, ushort> _lastSent = new Dictionary<int, ushort>();

        /// <summary>Actor numbers that have announced the mod. Re-announced periodically, so a late
        /// join or a missed packet costs a few seconds rather than staying wrong for the run.</summary>
        private readonly HashSet<int> _modded = new HashSet<int>();

        private ILog _log;
        private float _nextTick;
        private float _nextPresence;
        private int _lastPlayerCount;
        private string _room;

        internal void Initialize(ILog log)
        {
            _log = log;
            _instance = this;
            PhotonNetwork.AddCallbackTarget(this);
        }

        private void OnDestroy()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!PhotonNetwork.InRoom)
            {
                _room = null;
                return;
            }

            // A different room is a different set of actor numbers; nothing carries over.
            string room = PhotonNetwork.CurrentRoom == null ? null : PhotonNetwork.CurrentRoom.Name;
            if (room != _room)
            {
                _room = room;
                _modded.Clear();
                _lastSent.Clear();
                _lastPlayerCount = 0;
                _nextPresence = 0f;
            }

            AnnouncePresence();

            if (!BingBongSlot.Enabled) return;
            if (!PhotonNetwork.IsMasterClient) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickSeconds;

            // Someone joined: they have never seen any of these slots, so resend the lot.
            int playerCount = PhotonNetwork.CurrentRoom == null ? 0 : PhotonNetwork.CurrentRoom.PlayerCount;
            if (playerCount > _lastPlayerCount) _lastSent.Clear();
            _lastPlayerCount = playerCount;

            var characters = PlayerHandler.GetAllPlayerCharacters();
            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                if (character == null || character.player == null) continue;

                var slot = BingBongSlot.For(character.player);
                if (slot == null) continue;

                int viewId = ViewIdOf(character.player);
                if (viewId == 0) continue;

                ushort itemId = slot.prefab == null ? Empty : slot.prefab.itemID;

                ushort previous;
                if (_lastSent.TryGetValue(viewId, out previous) && previous == itemId) continue;

                _lastSent[viewId] = itemId;
                Send(viewId, slot);
            }
        }

        /// <summary>
        /// Says "I have the mod", on a timer rather than once on join. Repeating makes it
        /// self-healing: a client that joins late, or misses the packet, is known within seconds
        /// without anyone having to sequence a join handshake correctly.
        /// </summary>
        private void AnnouncePresence()
        {
            if (Time.unscaledTime < _nextPresence) return;
            _nextPresence = Time.unscaledTime + PresenceSeconds;

            try
            {
                PhotonNetwork.RaiseEvent(
                    PresenceEventCode,
                    null,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendReliable);
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("Could not announce the mod to the lobby: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether that player's machine knows about slot 249. The host must not route a Bing Bong
        /// into a slot the owner cannot see: their EquipSlot would call GetItemSlot(249), get null,
        /// and throw inside its own coroutine. Unknown means no, so a lobby is only ever wrong in
        /// the direction of behaving like vanilla.
        /// </summary>
        internal static bool HasMod(Player player)
        {
            if (player == null) return false;

            var view = player.photonView;
            if (view == null || view.Owner == null) return false;

            if (view.Owner.IsLocal) return true;                     // us; we are running this code
            if (_instance == null) return false;

            return _instance._modded.Contains(view.Owner.ActorNumber);
        }

        /// <summary>Pushes one player's slot immediately, without waiting for the next tick.</summary>
        internal static void Broadcast(Player player, ItemSlot slot)
        {
            if (_instance == null || player == null || slot == null) return;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

            int viewId = ViewIdOf(player);
            if (viewId == 0) return;

            _instance._lastSent[viewId] = slot.prefab == null ? Empty : slot.prefab.itemID;
            _instance.Send(viewId, slot);
        }

        private void Send(int viewId, ItemSlot slot)
        {
            ushort itemId = slot.prefab == null ? Empty : slot.prefab.itemID;
            byte[] guid = slot.data == null ? new byte[0] : slot.data.guid.ToByteArray();

            try
            {
                PhotonNetwork.RaiseEvent(
                    EventCode,
                    new object[] { viewId, (int)itemId, guid },
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendReliable);
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("Bing Bong slot sync failed: " + ex.Message);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == PresenceEventCode)
            {
                if (_modded.Add(photonEvent.Sender))
                    BingBongSlot.Trace("player " + photonEvent.Sender + " has the mod.");
                return;
            }

            if (photonEvent.Code != EventCode) return;

            var payload = photonEvent.CustomData as object[];
            if (payload == null || payload.Length < 3) return;

            try
            {
                int viewId = (int)payload[0];
                ushort itemId = (ushort)(int)payload[1];
                var guidBytes = payload[2] as byte[];

                var view = PhotonView.Find(viewId);
                if (view == null) return;

                var player = view.GetComponent<Player>();
                if (player == null) return;

                var slot = BingBongSlot.For(player);
                if (slot == null) return;

                if (itemId == Empty)
                {
                    slot.EmptyOut();
                    BingBongSlot.Trace("received: slot cleared for " + player.name + ".");
                    return;
                }

                Item prefab;
                if (!ItemDatabase.TryGetItem(itemId, out prefab)) return;

                slot.SetItem(prefab, ResolveData(guidBytes));
                BingBongSlot.Trace("received: " + prefab.name + " in the slot for " + player.name + ".");
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("Malformed Bing Bong slot event ignored: " + ex.Message);
            }
        }

        /// <summary>
        /// Mirrors what InventorySyncData does: reuse the instance data behind that guid if this
        /// client has it, otherwise register a fresh one under the same guid so identity still matches.
        /// </summary>
        private static ItemInstanceData ResolveData(byte[] guidBytes)
        {
            if (guidBytes == null || guidBytes.Length != 16) return new ItemInstanceData(Guid.NewGuid());

            var guid = new Guid(guidBytes);

            ItemInstanceData data;
            if (ItemInstanceDataHandler.TryGetInstanceData(guid, out data)) return data;

            data = new ItemInstanceData(guid);
            ItemInstanceDataHandler.AddInstanceData(data);
            return data;
        }

        private static int ViewIdOf(Player player)
        {
            var view = player.GetComponent<PhotonView>();
            return view == null ? 0 : view.ViewID;
        }
    }
}
