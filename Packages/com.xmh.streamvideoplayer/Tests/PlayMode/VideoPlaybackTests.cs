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
            float deadline = Time.realtimeSinceStartup + 20;
            bool complete = false;
            while (!complete && Time.realtimeSinceStartup < deadline)
            {
                complete = true;
                foreach (var session in sessions)
                {
                    session.Render();
                    var stats = session.GetStatistics();
                    Assert.AreNotEqual(VideoPlaybackState.Failed, stats.State, session.GetError());
                    complete &= stats.PresentedFrames >= 10;
                }
                yield return null;
            }
            Assert.IsTrue(complete, "Four independent decoders did not all produce frames.");
            foreach (var session in sessions) session.RequestStop();
            foreach (var session in sessions) { session.Dispose(); session.Dispose(); }
            sessions.Clear();
            yield return WaitForFrames(Open(source), 5);
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
