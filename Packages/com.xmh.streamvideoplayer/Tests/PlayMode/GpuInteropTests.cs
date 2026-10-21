using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace StreamVideoPlayer.Tests
{
    public sealed class GpuInteropTests
    {
        private uint initialProbeCount;

        [SetUp]
        public void RequireNativeD3D11()
        {
            var status = RuntimeDiagnostics.GetStatus();
            if (!status.GpuProbeSupported) Assert.Ignore(status.Message);
            initialProbeCount = status.ActiveProbes;
        }

        [TearDown]
        public void CheckNoNativeLeak()
        {
            Assert.That(RuntimeDiagnostics.GetStatus().ActiveProbes, Is.EqualTo(initialProbeCount));
        }

        [UnityTest]
        public IEnumerator NativeRenderEventWritesOpaquePixelsToUnityTexture()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU readback is needed only for this verification test.");
            using (var probe = new GpuProbeSession(32, 32))
            {
                probe.Render();
                for (int frame = 0; frame < 120 && probe.GetRenderedFrameCount() == 0; ++frame)
                    yield return null;
                Assert.That(probe.GetRenderedFrameCount(), Is.GreaterThan(0ul));

                // Readback is used by the test only. The runtime diagnostic has no CPU pixel path.
                var readback = AsyncGPUReadback.Request(probe.Texture, 0, TextureFormat.RGBA32);
                for (int frame = 0; frame < 120 && !readback.done; ++frame) yield return null;
                Assert.That(readback.done, Is.True, "GPU readback did not complete.");
                Assert.That(readback.hasError, Is.False);
                var pixel = readback.GetData<Color32>()[0];
                Assert.That(pixel.a, Is.EqualTo(255));
                Assert.That((int)pixel.r + pixel.g + pixel.b, Is.GreaterThan(30));
            }
        }

        [UnityTest]
        public IEnumerator QueuedEventsAfterDisposeDoNotTouchNewSessions()
        {
            for (int i = 0; i < 32; ++i)
            {
                var old = new GpuProbeSession(16, 16);
                old.Render();
                old.Dispose();
                old.Dispose();
                Assert.Throws<ObjectDisposedException>(() => old.Render());
            }
            using (var survivor = new GpuProbeSession(16, 16))
            {
                yield return null;
                yield return null;
                Assert.That(survivor.GetRenderedFrameCount(), Is.EqualTo(0ul), "Stale events rendered into a new session.");
                survivor.Render();
                for (int frame = 0; frame < 120 && survivor.GetRenderedFrameCount() == 0; ++frame)
                    yield return null;
                Assert.That(survivor.GetRenderedFrameCount(), Is.EqualTo(1ul));
            }
        }

        [UnityTest]
        public IEnumerator MultipleSessionsRenderAndReleaseIndependently()
        {
            var probes = new GpuProbeSession[4];
            try
            {
                for (int i = 0; i < probes.Length; ++i) probes[i] = new GpuProbeSession(16, 16);
                Assert.That(RuntimeDiagnostics.GetStatus().ActiveProbes, Is.EqualTo(initialProbeCount + 4));
                foreach (var probe in probes) probe.Render();
                for (int frame = 0; frame < 120 && probes[3].GetRenderedFrameCount() == 0; ++frame)
                    yield return null;
                foreach (var probe in probes) Assert.That(probe.GetRenderedFrameCount(), Is.EqualTo(1ul));
                probes[0].Dispose();
                Assert.That(RuntimeDiagnostics.GetStatus().ActiveProbes, Is.EqualTo(initialProbeCount + 3));
                probes[1].Render();
                for (int frame = 0; frame < 120 && probes[1].GetRenderedFrameCount() == 1; ++frame)
                    yield return null;
                Assert.That(probes[1].GetRenderedFrameCount(), Is.EqualTo(2ul));
            }
            finally { foreach (var probe in probes) probe?.Dispose(); }
        }
    }
}
