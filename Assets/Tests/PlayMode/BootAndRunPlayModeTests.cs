using System.Collections;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    /// <summary>
    /// Acceptance: boot Main.unity, run 500 ticks, assert GameTime state and no
    /// exceptions. Also exercises pause and time-scale behavior at the ECS level
    /// (keyboard plumbing itself is verified manually / in the editor).
    /// </summary>
    [TestFixture]
    public class BootAndRunPlayModeTests
    {
        [UnityTest]
        public IEnumerator MainScene_Boots_Runs500Ticks_GameTimeStaysConsistent()
        {
            bool sawException = false;
            string firstException = null;
            Application.LogCallback logHandler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception)
                {
                    sawException = true;
                    if (firstException == null) firstException = condition + "\n" + stackTrace;
                }
            };
            Application.logMessageReceived += logHandler;

            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
                while (!load.isDone)
                {
                    yield return null;
                }
                yield return null; // first full frame after Awake.

                World world = World.DefaultGameObjectInjectionWorld;
                Assert.IsNotNull(world, "WorldBootstrap must create/adopt the default injection world.");

                EntityManager em = world.EntityManager;
                EntityQuery controlQuery = em.CreateEntityQuery(typeof(TimeControl));
                EntityQuery timeQuery = em.CreateEntityQuery(typeof(GameTime));
                EntityQuery settingsQuery = em.CreateEntityQuery(typeof(WorldSettingsData));
                Assert.AreEqual(1, controlQuery.CalculateEntityCount(), "TimeControl singleton missing.");
                Assert.AreEqual(1, timeQuery.CalculateEntityCount(), "GameTime singleton missing.");
                Assert.AreEqual(1, settingsQuery.CalculateEntityCount(), "WorldSettingsData singleton missing.");

                // Fast-forward at x256: cap is 8 ticks/frame => 500 ticks in ~63 frames.
                TimeControl control = controlQuery.GetSingleton<TimeControl>();
                control.Paused = 0;
                control.TimeScaleIndex = CalendarMath.TimeScaleCount - 1;
                controlQuery.SetSingleton(control);

                int frames = 0;
                while (timeQuery.GetSingleton<GameTime>().TotalTicks < 500UL && frames < 3000)
                {
                    frames++;
                    yield return null;
                }

                GameTime time = timeQuery.GetSingleton<GameTime>();
                Assert.GreaterOrEqual(time.TotalTicks, 500UL,
                    $"500 ticks must be reached well within 3000 frames at x256 (took {time.TotalTicks}).");

                WorldSettingsData settings = settingsQuery.GetSingleton<WorldSettingsData>();
                SimDate expected = CalendarMath.FromTicks(time.TotalTicks, settings.ToClockConfig());
                Assert.AreEqual(expected.Year, time.Year);
                Assert.AreEqual(expected.DayOfYear, time.DayOfYear);
                Assert.AreEqual(expected.DayOfSeason, time.DayOfSeason);
                Assert.AreEqual(expected.Season, time.Season);
                Assert.AreEqual(expected.TickOfDay, time.TickOfDay);
                Assert.AreEqual(expected.AbsoluteDay, time.AbsoluteDay);
                Assert.GreaterOrEqual(time.DayFraction, 0f);
                Assert.Less(time.DayFraction, 1f);

                // Pause must freeze the clock.
                control = controlQuery.GetSingleton<TimeControl>();
                control.Paused = 1;
                controlQuery.SetSingleton(control);
                ulong pausedTicks = 0UL;
                for (int i = 0; i < 20; i++)
                {
                    pausedTicks += timeQuery.GetSingleton<GameTime>().TotalTicks;
                    yield return null;
                }
                Assert.AreEqual(timeQuery.GetSingleton<GameTime>().TotalTicks * 20UL, pausedTicks,
                    "GameTime must not advance while paused.");

                // Resume at x1 must advance again.
                control = controlQuery.GetSingleton<TimeControl>();
                control.Paused = 0;
                control.TimeScaleIndex = CalendarMath.DefaultTimeScaleIndex;
                controlQuery.SetSingleton(control);
                ulong before = timeQuery.GetSingleton<GameTime>().TotalTicks;
                frames = 0;
                while (timeQuery.GetSingleton<GameTime>().TotalTicks == before && frames < 600)
                {
                    frames++;
                    yield return null;
                }
                Assert.Greater(timeQuery.GetSingleton<GameTime>().TotalTicks, before,
                    "GameTime must advance after resume.");

                controlQuery.Dispose();
                timeQuery.Dispose();
                settingsQuery.Dispose();
            }
            finally
            {
                Application.logMessageReceived -= logHandler;
            }

            Assert.IsFalse(sawException, "Exceptions during boot/run:\n" + firstException);
        }
    }
}
