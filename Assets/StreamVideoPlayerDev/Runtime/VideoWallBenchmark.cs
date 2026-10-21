using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using StreamVideoPlayer.Samples;
using UnityEngine;

namespace StreamVideoPlayer.Development
{
    public sealed class VideoWallBenchmark : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)] private struct ProcessMemoryCounters
        {
            public uint Size, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
        private static double WorkingSetMB()
        {
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            return GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size)
                ? counters.WorkingSetSize.ToUInt64() / 1048576.0 : -1;
        }
        [Serializable] private sealed class StreamResult
        {
            public int index, width, height;
            public double decodedFps, presentedFps, firstPresentedMs;
            public ulong droppedFrames;
            public bool skippedProbe;
            public bool performanceAvailable;
            public ulong writerBusyDrops, readerBusySkips, inputViewCreations;
            public double conversionCpuMeanUs, presentationCpuMeanUs;
        }
        [Serializable] private sealed class Result
        {
            public bool success;
            public bool batchRendering;
            public string error, gpu, unityVersion;
            public int sessions;
            public double measurementSeconds, unityFps, frameP95Ms, frameP99Ms, processCpuPercentOfMachine, peakWorkingSetMB, stopAllMs;
            public double renderSubmissionMeanUs;
            public StreamResult[] streams;
        }
        private static int ArgumentInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value) ? value : fallback;
        }
        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return 0;
            values.Sort(); return values[(int)((values.Count - 1) * p)];
        }
        private VideoWallDemo wall;
        private IEnumerator Start()
        {
            if (Application.isEditor) yield break;
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-svp-wall-benchmark");
            if (flag < 0 || flag + 1 >= args.Length) yield break;
            string output = args[flag + 1];
            int count = ArgumentInt(args, "-svp-count", 4), duration = ArgumentInt(args, "-svp-seconds", 15);
            var result = new Result { gpu = SystemInfo.graphicsDeviceName, unityVersion = Application.unityVersion, sessions = count, peakWorkingSetMB = -1 };
            if (count < 1 || count > 16 || duration < 2 || duration > 300)
            {
                result.error = "Count must be 1..16 and seconds 2..300.";
                Finish(output, result); yield break;
            }
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            wall = GetComponent<VideoWallDemo>();
            wall.BatchRendering = Array.IndexOf(args, "-svp-individual-render") < 0;
            result.batchRendering = wall.BatchRendering;
            wall.streamCount = count;
            int sourceFlag = Array.IndexOf(args, "-svp-source");
            if (sourceFlag >= 0 && sourceFlag + 1 < args.Length) wall.source = args[sourceFlag + 1];
            wall.Play();
            var clock = Stopwatch.StartNew();
            bool ready = false;
            while (!ready && clock.Elapsed.TotalSeconds < 20)
            {
                ready = wall.Sessions.Count == count;
                if (wall.LastError != null) { result.error = wall.LastError; break; }
                foreach (var session in wall.Sessions)
                {
                    var stats = session.GetStatistics();
                    if (stats.State == VideoPlaybackState.Failed) { result.error = session.GetError(); break; }
                    ready &= stats.PresentedFrames >= 5;
                }
                if (result.error != null) break;
                yield return null;
            }
            if (!ready && result.error == null) result.error = "Not all sessions presented within 20 seconds.";
            if (result.error != null) { wall.Stop(); Finish(output, result); yield break; }
            yield return new WaitForSecondsRealtime(2);
            var before = new VideoStatistics[count];
            var performanceBefore = new VideoPerformanceStatistics[count];
            for (int i = 0; i < count; ++i)
            {
                before[i] = wall.Sessions[i].GetStatistics();
                wall.Sessions[i].TryGetPerformanceStatistics(out performanceBefore[i]);
            }
            var intervals = new List<double>(duration * 75);
            double submissionBefore = wall.RenderSubmissionCpuMs;
            using (var process = Process.GetCurrentProcess())
            {
                double cpuBefore = process.TotalProcessorTime.TotalSeconds;
                clock.Restart(); double previous = 0, nextSample = 0;
                while (clock.Elapsed.TotalSeconds < duration)
                {
                    yield return null;
                    double now = clock.Elapsed.TotalSeconds;
                    intervals.Add((now - previous) * 1000); previous = now;
                    if (wall.LastError != null || wall.Sessions.Count != count) { result.error = wall.LastError ?? "A session disappeared."; break; }
                    if (now >= nextSample)
                    {
                        process.Refresh();
                        // Unity Mono can report zero for Process.WorkingSet64; use the Windows API.
                        result.peakWorkingSetMB = Math.Max(result.peakWorkingSetMB, WorkingSetMB());
                        nextSample = now + 0.5;
                    }
                }
                result.measurementSeconds = clock.Elapsed.TotalSeconds;
                process.Refresh();
                result.processCpuPercentOfMachine = (process.TotalProcessorTime.TotalSeconds - cpuBefore) / result.measurementSeconds / Environment.ProcessorCount * 100;
            }
            result.unityFps = intervals.Count / result.measurementSeconds;
            result.renderSubmissionMeanUs = (wall.RenderSubmissionCpuMs - submissionBefore) * 1000 / Math.Max(1, intervals.Count);
            result.frameP95Ms = Percentile(intervals, .95); result.frameP99Ms = Percentile(intervals, .99);
            result.streams = new StreamResult[count];
            if (wall.Sessions.Count == count)
                for (int i = 0; i < count; ++i)
                {
                    var session = wall.Sessions[i]; var stats = session.GetStatistics();
                    if (stats.State == VideoPlaybackState.Failed) result.error = session.GetError();
                    if (!session.TryGetStartupTimings(out var timings)) result.error = "Old native DLL loaded: build/install the current plugin before benchmarking.";
                    if (stats.PresentedFrames == before[i].PresentedFrames) result.error = "A stream stopped presenting during measurement.";
                    result.streams[i] = new StreamResult
                    {
                        index = i, width = stats.SourceWidth, height = stats.SourceHeight,
                        decodedFps = (stats.DecodedFrames - before[i].DecodedFrames) / result.measurementSeconds,
                        presentedFps = (stats.PresentedFrames - before[i].PresentedFrames) / result.measurementSeconds,
                        droppedFrames = stats.DroppedFrames - before[i].DroppedFrames,
                        firstPresentedMs = timings.FirstPresentedUs / 1000.0, skippedProbe = timings.SkippedStreamProbe != 0
                    };
                    var row = result.streams[i];
                    row.performanceAvailable = session.TryGetPerformanceStatistics(out var performance);
                    if (row.performanceAvailable)
                    {
                        var baseline = performanceBefore[i];
                        row.writerBusyDrops = performance.WriterBusyDrops - baseline.WriterBusyDrops;
                        row.readerBusySkips = performance.ReaderBusySkips - baseline.ReaderBusySkips;
                        row.inputViewCreations = performance.InputViewCreations - baseline.InputViewCreations;
                        row.conversionCpuMeanUs = (double)(performance.ConversionCpuUs - baseline.ConversionCpuUs) / Math.Max(1ul, performance.ConversionAttempts - baseline.ConversionAttempts);
                        row.presentationCpuMeanUs = (double)(performance.PresentationCpuUs - baseline.PresentationCpuUs) / Math.Max(1ul, performance.PresentCalls - baseline.PresentCalls);
                    }
                }
            clock.Restart(); wall.Stop(); result.stopAllMs = clock.Elapsed.TotalMilliseconds;
            result.success = result.error == null;
            Finish(output, result);
        }
        private static void Finish(string path, Result result)
        {
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            Application.Quit(result.success ? 0 : 1);
        }
        private void OnDisable() { if (wall != null) wall.Stop(); }
    }
}
