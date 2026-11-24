using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace ChronoWild.EditorTools
{
    /// <summary>
    /// Records Profiler frames in Play Mode and summarises the main-thread hierarchy: self time and
    /// GC allocations per marker (scripts appear as "Class.Update() [Invoke]"), averaged per frame.
    /// The raw capture is saved next to the summary (.data) so it can be opened in the Profiler.
    /// </summary>
    public static class ProfilerReport
    {
        public static void Start()
        {
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.deepProfiling = false;
            ProfilerDriver.enabled = true;
        }

        public static void Stop() => ProfilerDriver.enabled = false;

        sealed class Entry
        {
            public double self, total, gc, calls;
            public string parent;
        }

        public static string Dump(string jsonPath, int maxFrames = 300, string filter = null)
        {
            int last = ProfilerDriver.lastFrameIndex;
            int first = Mathf.Max(ProfilerDriver.firstFrameIndex, last - maxFrames + 1);
            var entries = new Dictionary<string, Entry>();
            int frames = 0;
            var children = new List<int>();

            string[] counterNames = { "Batches Count", "Draw Calls Count", "SetPass Calls Count", "Triangles Count",
                "Shadow Casters Count", "Render Textures Count", "GC Allocated In Frame", "GC Allocation In Frame Count", "Total Used Memory", "GC Used Memory" };
            var counterSums = new double[counterNames.Length];
            var counterMax = new double[counterNames.Length];
            var counterFrames = new int[counterNames.Length];
            for (int f = first; f <= last; f++)
            {
                using (var raw = ProfilerDriver.GetRawFrameDataView(f, 0))
                    if (raw != null && raw.valid)
                        for (int c = 0; c < counterNames.Length; c++)
                        {
                            int id = raw.GetMarkerId(counterNames[c]);
                            if (id == FrameDataView.invalidMarkerId)
                                continue;
                            double v = raw.GetCounterValueAsDouble(id);
                            counterSums[c] += v;
                            counterMax[c] = System.Math.Max(counterMax[c], v);
                            counterFrames[c]++;
                        }
                using var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false);
                if (view == null || !view.valid)
                    continue;
                frames++;
                Walk(view, view.GetRootItemID(), "");
            }

            void Walk(HierarchyFrameDataView view, int id, string parentName)
            {
                var list = new List<int>();
                view.GetItemChildren(id, list);
                foreach (var child in list)
                {
                    string name = view.GetItemName(child);
                    if (!entries.TryGetValue(name, out var e))
                        entries[name] = e = new Entry { parent = parentName };
                    e.self += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                    e.total += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                    e.gc += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                    e.calls += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                    Walk(view, child, name);
                }
            }

            string Row(KeyValuePair<string, Entry> p) => string.Format(CultureInfo.InvariantCulture,
                "{{\"name\": {0}, \"parent\": {1}, \"selfMs\": {2:F4}, \"totalMs\": {3:F4}, \"gcKB\": {4:F3}, \"calls\": {5:F1} }}",
                Quote(p.Key), Quote(p.Value.parent), p.Value.self / frames, p.Value.total / frames, p.Value.gc / frames / 1024.0, p.Value.calls / frames);

            var sb = new StringBuilder();
            sb.Append("{\n  \"frames\": ").Append(frames).Append(",\n  \"counters\": {");
            var counterRows = new List<string>();
            for (int c = 0; c < counterNames.Length; c++)
                if (counterFrames[c] > 0)
                    counterRows.Add(string.Format(CultureInfo.InvariantCulture, "\"{0}\": {{\"avg\": {1:F1}, \"max\": {2:F1} }}",
                        counterNames[c], counterSums[c] / counterFrames[c], counterMax[c]));
            sb.Append(string.Join(", ", counterRows)).Append("},\n  \"topSelf\": [\n    ");
            sb.Append(string.Join(",\n    ", entries.OrderByDescending(p => p.Value.self).Take(30).Select(Row)));
            sb.Append("\n  ],\n  \"topGC\": [\n    ");
            sb.Append(string.Join(",\n    ", entries.Where(p => p.Value.gc > 0).OrderByDescending(p => p.Value.gc).Take(20).Select(Row)));
            sb.Append("\n  ],\n  \"scripts\": [\n    ");
            sb.Append(string.Join(",\n    ", entries.Where(p => p.Key.Contains("[Invoke]") || p.Key.Contains(".Update") || p.Key.Contains("OnGUI"))
                .OrderByDescending(p => p.Value.total).Take(25).Select(Row)));
            sb.Append("\n  ]");
            if (!string.IsNullOrEmpty(filter))
            {
                var terms = filter.Split('|');
                sb.Append(",\n  \"filtered\": [\n    ");
                sb.Append(string.Join(",\n    ", entries.Where(p => terms.Any(t => p.Key.Contains(t))).OrderByDescending(p => p.Value.total).Take(25).Select(Row)));
                sb.Append("\n  ]");
            }
            sb.Append("\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
            File.WriteAllText(jsonPath, sb.ToString());
            ProfilerDriver.SaveProfile(Path.ChangeExtension(jsonPath, ".data"));
            return $"frames={frames}";
        }

        static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Screenshot of an open editor window (e.g. the Profiler) read back from the screen.</summary>
        public static string CaptureWindow(string typeName, string pngPath)
        {
            var type = typeof(EditorWindow).Assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null)
                return "no type";
            var window = EditorWindow.GetWindow(type);
            window.Show();
            window.Repaint();
            var rect = window.position;
            var pixels = InternalEditorUtility.ReadScreenPixel(rect.position, (int)rect.width, (int)rect.height);
            var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(pngPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return $"{rect}";
        }
    }
}
