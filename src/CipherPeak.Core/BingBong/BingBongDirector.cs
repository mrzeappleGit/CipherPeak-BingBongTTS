using System.Collections.Generic;
using CipherPeak.Core.Logging;

namespace CipherPeak.Core.BingBong
{
    /// <summary>
    /// The seam between the "exactly two Bing Bongs" rule and Unity/Photon.
    /// A handle is a Photon view id in the real game and an arbitrary int in tests.
    /// </summary>
    public interface IBingBongWorld
    {
        /// <summary>True only on the host, inside a playable run, with the world ready to spawn into.</summary>
        bool CanManage { get; }

        /// <summary>
        /// One handle per Bing Bong the mod is responsible for, and each handle is where its voice
        /// should come from: the Bing Bong's own view when it is standing in the world, or the
        /// carrier's view when a scout has it in hand or in a pocket. A stowed Bing Bong has no world
        /// object of its own - that is how PEAK pockets anything - so speaking through the scout who
        /// carries it is what lets a pocketed Bing Bong still talk.
        /// </summary>
        IReadOnlyList<int> FindManaged();

        /// <summary>Spawns one mod-tagged Bing Bong. Returns its handle, or 0 if it could not spawn yet.</summary>
        int Spawn();

        void Despawn(int handle);

        /// <summary>False once the entity is destroyed, out of bounds, or too far from every scout.
        /// Implementations keep a mid-sentence Bing Bong alive so replacement never cuts audio.</summary>
        bool IsAlive(int handle);

    }

    /// <summary>
    /// Keeps exactly two mod-controlled Bing Bongs alive. Every path (fresh run, scene reload,
    /// reconnect, host migration, late join) runs through the same reconcile step, which is why
    /// duplicates cannot accumulate: existing tagged entities are adopted before any new one is spawned.
    /// </summary>
    public sealed class BingBongDirector
    {
        public const int TargetCount = 2;

        private readonly IBingBongWorld _world;
        private readonly ILog _log;
        private readonly List<int> _handles = new List<int>(TargetCount);

        public BingBongDirector(IBingBongWorld world, ILog log = null)
        {
            _world = world;
            _log = log ?? NullLog.Instance;
        }

        public IReadOnlyList<int> Handles => _handles;
        public int Count => _handles.Count;

        /// <summary>Handles in a stable order, so speaker alternation is consistent across ticks.</summary>
        public bool TryGetHandle(int index, out int handle)
        {
            if (index < 0 || index >= _handles.Count) { handle = 0; return false; }
            handle = _handles[index];
            return true;
        }

        public void Tick()
        {
            if (!_world.CanManage) return;

            Adopt();
            DropDead();
            TrimExtras();
            TopUp();
        }

        /// <summary>Adopt tagged entities we are not tracking yet. This is what makes reconnects and
        /// scene reloads idempotent instead of duplicating.</summary>
        private void Adopt()
        {
            var existing = _world.FindManaged();
            if (existing == null) return;

            for (int i = 0; i < existing.Count; i++)
            {
                int handle = existing[i];
                if (handle == 0 || _handles.Contains(handle)) continue;

                // Adopting one that already fails the liveness rule adopts it straight back out again
                // on the same tick, and the next tick finds it and repeats - forever, for anything the
                // director is not allowed to destroy. Something out of reach is simply left alone.
                if (!_world.IsAlive(handle)) continue;

                _handles.Add(handle);
                _log.Info("Adopted existing mod Bing Bong " + handle);
            }
        }

        private void DropDead()
        {
            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                if (_world.IsAlive(_handles[i])) continue;
                _log.Info("Bing Bong " + _handles[i] + " is gone or out of bounds; scheduling replacement.");
                // Despawn is safe on an already-destroyed handle and cleans up the "too far away" case.
                _world.Despawn(_handles[i]);
                _handles.RemoveAt(i);
            }
        }

        private void TrimExtras()
        {
            while (_handles.Count > TargetCount)
            {
                int last = _handles.Count - 1;
                int handle = _handles[last];
                _handles.RemoveAt(last);
                _world.Despawn(handle);
                _log.Warn("Removed surplus mod Bing Bong " + handle);
            }
        }

        private void TopUp()
        {
            while (_handles.Count < TargetCount)
            {
                int handle = _world.Spawn();
                if (handle == 0) return;             // world not ready; retry next tick
                if (_handles.Contains(handle)) return; // defensive: never track the same entity twice
                _handles.Add(handle);
                _log.Info("Spawned mod Bing Bong " + handle + " (" + _handles.Count + " of " + TargetCount + ")");
            }
        }

        /// <summary>Run ended or mod disabled: remove everything we own.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _handles.Count; i++)
                _world.Despawn(_handles[i]);
            _handles.Clear();
        }

        /// <summary>Scene teardown destroyed our entities for us; forget them without despawning.</summary>
        public void Forget()
        {
            _handles.Clear();
        }
    }
}
