using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace StreamVideoPlayer.Tests
{
    public sealed class VideoPlaybackTests
    {
        private readonly List<VideoStreamSession> sessions = new List<VideoStreamSession>();
        private RenderTexture readable;
        private string Source()
        {
            var source = Environment.GetEnvironmentVariable("SVP_TEST_RTSP_URL");
            if (string.IsNullOrEmpty(source)) Assert.Ignore("Set SVP_TEST_RTSP_URL to an H.264 test-pattern RTSP stream to run video integration tests.");
            return source;
        }
        private VideoStreamSession Open(string source)
        {
            var session = new VideoStreamSession(source, 320, 180);
            sessions.Add(session);
            return session;
        }
        private IEnumerator WaitForFrames(VideoStreamSession session, ulong count)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < deadline)
            {
                session.Render();
                var stats = session.GetStatistics();
                Assert.AreNotEqual(VideoPlaybackState.Failed, stats.State, session.GetError());
                if (stats.PresentedFrames >= count) yield break;
                yield return null;
            }
            Assert.Fail("Timed out waiting for actual video frames.");
        }
        [UnityTest]
        public IEnumerator RtspHardwareDecodeProducesNonUniformPixels()
        {
            var session = Open(Source());
            yield return WaitForFrames(session, 10);
            Assert.Greater(session.GetStatistics().SourceWidth, 0);
            if (session.TryGetStartupTimings(out var timings))
            {
                Assert.Greater(timings.DeviceReadyUs, 0ul);
                Assert.GreaterOrEqual(timings.InputOpenUs, timings.DeviceReadyUs);
                Assert.GreaterOrEqual(timings.StreamInfoUs, timings.InputOpenUs);
                Assert.GreaterOrEqual(timings.DecoderReadyUs, timings.StreamInfoUs);
                Assert.GreaterOrEqual(timings.FirstDecodedUs, timings.DecoderReadyUs);
                Assert.GreaterOrEqual(timings.FirstPresentedUs, timings.FirstDecodedUs);
            }
            // BGRA sRGB sampling is supported, but Unity 2022 D3D11 cannot read it back directly.
            // This GPU blit/readback is verification-only; production playback never reads pixels to CPU.
            readable = new RenderTexture(320, 180, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            readable.Create();
            Graphics.Blit(session.Texture, readable);
            var request = AsyncGPUReadback.Request(readable, 0, TextureFormat.RGBA32);
            float deadline = Time.realtimeSinceStartup + 5;
            while (!request.done && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(request.done); Assert.IsFalse(request.hasError);
            var pixels = request.GetData<Color32>();
            int min = 765, max = 0;
            for (int i = 0; i < pixels.Length; i += 17)
            {
                int sum = pixels[i].r + pixels[i].g + pixels[i].b;
                min = Math.Min(min, sum); max = Math.Max(max, sum);
            }
            Assert.Greater(max - min, 100, "Expected a visible test pattern, not a flat color or black texture.");
        }
        [UnityTest]
        public IEnumerator FourRtspSessionsRenderAndCanBeReopened()
        {
            string source = Source();
            for (int i = 0; i < 4; ++i) Open(source);
            using var batch = new VideoStreamRenderBatch(sessions);
            float deadline = Time.realtimeSinceStartup + 20;
            bool complete = false;
            while (!complete && Time.realtimeSinceStartup < deadline)
            {
                complete = true;
                batch.Render();
                foreach (var session in sessions)
                {
                    var stats = session.GetStatistics();
                    Assert.AreNotEqual(VideoPlaybackState.Failed, stats.State, session.GetError());
                    complete &= stats.PresentedFrames >= 10;
                }
                yield return null;
            }
            Assert.IsTrue(complete, "Four independent decoders did not all produce frames.");
            // Submit first, then retire a handle while the render event may still be queued.
            batch.Render();
            sessions[0].Dispose();
            for (int i = 0; i < 3; ++i) { batch.Render(); yield return null; }
            for (int i = 1; i < sessions.Count; ++i)
                Assert.AreNotEqual(VideoPlaybackState.Failed, sessions[i].GetStatistics().State, sessions[i].GetError());
            foreach (var session in sessions) session.RequestStop();
            foreach (var session in sessions) { session.Dispose(); session.Dispose(); }
            sessions.Clear();
            var replacement = Open(source);
            for (int i = 0; i < 3; ++i) { batch.Render(); yield return null; }
            Assert.AreEqual(0ul, replacement.GetStatistics().PresentedFrames, "A stale batch rendered a replacement session.");
            yield return WaitForFrames(replacement, 5);
        }
        [UnityTest]
        public IEnumerator PausedRendererSkipsOldFramesAndReusesInputViews()
        {
            var session = Open(Source());
            yield return WaitForFrames(session, 30);
            // Drain the final queued event from WaitForFrames before taking the paused baseline.
            yield return null;
            yield return null;
            Assert.IsTrue(session.TryGetPerformanceStatistics(out var before), "Current native performance API is required.");
            Assert.AreEqual(64, System.Runtime.InteropServices.Marshal.SizeOf<VideoPerformanceStatistics>());
            var presentedBefore = session.GetStatistics().PresentedFrames;
            // Decode continues while Unity deliberately submits no presentation events.
            yield return new WaitForSecondsRealtime(1);
            Assert.IsTrue(session.TryGetPerformanceStatistics(out var after));
            Assert.Greater(after.ConvertedFrames, before.ConvertedFrames + 5, "Producer stalled behind a paused renderer.");
            Assert.Less(after.InputViewCreations - before.InputViewCreations, after.ConvertedFrames - before.ConvertedFrames,
                "Decoder input views were recreated per frame instead of reused.");
            Assert.AreEqual(presentedBefore, session.GetStatistics().PresentedFrames);
            // Stop production and allow submitted GPU work to settle, then consume its latest frame.
            session.RequestStop();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return WaitForFrames(session, presentedBefore + 1);
            session.TryGetPerformanceStatistics(out var finalPerformance);
            var finalStats = session.GetStatistics();
            Assert.AreEqual(finalPerformance.ConvertedFrames - finalStats.PresentedFrames + finalPerformance.WriterBusyDrops,
                finalStats.DroppedFrames, "Presentation did not jump directly to the newest available generation.");
            for (int i = 0; i < 5; ++i) { session.Render(); yield return null; }
            Assert.AreEqual(finalStats.PresentedFrames, session.GetStatistics().PresentedFrames, "Old mailbox slots were replayed after the newest frame.");
        }
        [UnityTest]
        public IEnumerator ConnectionFailureIsReportedWithoutCredentials()
        {
            Source(); // Explicit opt-in for integration tests / installed native dependency.
            var session = Open("rtsp://test-user:test-secret@127.0.0.1:1/missing");
            float deadline = Time.realtimeSinceStartup + 8;
            while (session.GetStatistics().State != VideoPlaybackState.Failed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(VideoPlaybackState.Failed, session.GetStatistics().State);
            Assert.IsNotEmpty(session.GetError());
            StringAssert.DoesNotContain("test-secret", session.GetError());
            StringAssert.DoesNotContain("test-user", session.GetError());
        }
        [TearDown]
        public void Cleanup()
        {
            foreach (var session in sessions) session.RequestStop();
            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
            if (readable != null) { readable.Release(); UnityEngine.Object.DestroyImmediate(readable); readable = null; }
        }
    }
}
