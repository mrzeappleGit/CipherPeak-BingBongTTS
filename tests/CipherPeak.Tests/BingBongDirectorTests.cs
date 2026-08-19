using CipherPeak.Core.BingBong;
using Xunit;

namespace CipherPeak.Tests
{
    public class BingBongDirectorTests
    {
        // Adopting something that already fails the liveness rule adopts it straight back out on the
        // same tick, and finds it again on the next one. For anything the director may not destroy,
        // that loop never ends.
        [Fact]
        public void DoesNotAdoptSomethingOutOfReach()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            world.PlantOutOfReach();

            director.Tick();
            int spawnsAfterFirst = world.SpawnCalls;
            director.Tick();
            director.Tick();

            Assert.Equal(2, director.Count);                    // its own two, not the stranded one
            Assert.Equal(spawnsAfterFirst, world.SpawnCalls);   // and no churn on later ticks
        }




        [Fact]
        public void SpawnsExactlyTwoOnTheFirstTick()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);

            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
            Assert.Equal(2, world.SpawnCalls);
        }

        [Fact]
        public void RepeatedTicksDoNotCreateMore()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);

            for (int i = 0; i < 10; i++) director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
            Assert.Equal(2, world.SpawnCalls);
        }

        [Fact]
        public void ReplacesALostBingBongWithoutDuplicating()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            int lost = director.Handles[0];
            world.Lose(lost);
            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
            Assert.DoesNotContain(lost, director.Handles);
        }

        [Fact]
        public void ReplacesBothWhenBothAreLost()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            foreach (var handle in new[] { director.Handles[0], director.Handles[1] }) world.Lose(handle);
            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
        }

        [Fact]
        public void DoesNothingWhenNotAllowedToManage()
        {
            var world = new FakeBingBongWorld { CanManage = false };
            var director = new BingBongDirector(world);

            director.Tick();

            Assert.Equal(0, director.Count);
            Assert.Equal(0, world.SpawnCalls);
        }

        [Fact]
        public void AdoptsEntitiesThatAlreadyExistInsteadOfSpawningNewOnes()
        {
            // Reconnect / scene reload / host migration: two of ours are already in the world.
            var world = new FakeBingBongWorld();
            world.PlantExisting();
            world.PlantExisting();

            var director = new BingBongDirector(world);
            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
            Assert.Equal(0, world.SpawnCalls);
        }

        [Fact]
        public void AdoptsOneAndSpawnsTheMissingOne()
        {
            var world = new FakeBingBongWorld();
            world.PlantExisting();

            var director = new BingBongDirector(world);
            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(1, world.SpawnCalls);
        }

        [Fact]
        public void RemovesSurplusEntities()
        {
            // Something spawned a third tagged Bing Bong behind our back.
            var world = new FakeBingBongWorld();
            world.PlantExisting();
            world.PlantExisting();
            world.PlantExisting();

            var director = new BingBongDirector(world);
            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
            Assert.Equal(1, world.DespawnCalls);
        }

        [Fact]
        public void RetriesLaterWhenSpawningIsNotPossibleYet()
        {
            var world = new FakeBingBongWorld { SpawnFails = true };
            var director = new BingBongDirector(world);

            director.Tick();
            Assert.Equal(0, director.Count);

            world.SpawnFails = false;
            director.Tick();
            Assert.Equal(2, director.Count);
        }

        [Fact]
        public void ReleaseAllRemovesEverythingItOwns()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            director.ReleaseAll();

            Assert.Equal(0, director.Count);
            Assert.Equal(0, world.AliveCount);
        }

        [Fact]
        public void ForgetLeavesEntitiesForTheNextHostToAdopt()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            director.Forget();

            Assert.Equal(0, director.Count);
            Assert.Equal(2, world.AliveCount);

            // The new host adopts them rather than spawning duplicates.
            var newHost = new BingBongDirector(world);
            newHost.Tick();

            Assert.Equal(2, newHost.Count);
            Assert.Equal(2, world.AliveCount);
        }

        [Fact]
        public void SceneReloadThatDestroysEverythingRebuildsExactlyTwo()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            foreach (var handle in new[] { director.Handles[0], director.Handles[1] }) world.Lose(handle);
            director.Forget();

            director.Tick();

            Assert.Equal(2, director.Count);
            Assert.Equal(2, world.AliveCount);
        }

        [Fact]
        public void LosingAuthorityMidRunDoesNotDespawnAnything()
        {
            var world = new FakeBingBongWorld();
            var director = new BingBongDirector(world);
            director.Tick();

            world.CanManage = false;
            director.Tick();

            Assert.Equal(2, world.AliveCount);
            Assert.Equal(0, world.DespawnCalls);
        }
    }
}
