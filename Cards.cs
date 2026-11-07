using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace TempMonitor
{
    internal enum DeviceKind { Processor, Graphics, Memory, Drive, Motherboard, Other }

    internal sealed class SensorView
    {
        public string Id;
        public string Name;
        public float? Value, Min, Max;
        public Status Status;
    }

    /// <summary>One device (CPU, GPU, drive, ...) as shown on a card.</summary>
    internal sealed class DeviceCard
    {
        public string Key;
        public string Name;
        public DeviceKind Kind;
        public SensorView Primary;                       // the headline reading
        public List<SensorView> Others = new List<SensorView>();
        public float[] History;                          // recent primary values, oldest first (NaN = missing)
        public Status Status;                            // worst status across all of this card's sensors

        public string KindLabel { get { return Cards.KindLabel(Kind); } }
    }

    internal static class Cards
    {
        public const int HistoryLength = 120; // samples, one per second

        private static readonly Dictionary<string, Queue<float>> s_history = new Dictionary<string, Queue<float>>();
        private static readonly Dictionary<string, float[]> s_minMax = new Dictionary<string, float[]>();

        private static readonly string[] PrimaryNames =
        {
            "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "GPU Core",
            "Composite Temperature", "Temperature", "Core Max",
        };

        /// <param name="newSample">True for a fresh sensor poll: adds it to the trend history and min/max.
        /// False when only redrawing (e.g. after switching °C/°F).</param>
        public static List<DeviceCard> Build(List<Reading> readings, bool newSample)
        {
            var cards = new List<DeviceCard>();
            var byKey = new Dictionary<string, DeviceCard>();

            foreach (Reading r in readings)
            {
                DeviceCard card;
                if (!byKey.TryGetValue(r.GroupKey, out card))
                {
                    card = new DeviceCard { Key = r.GroupKey, Name = r.GroupName, Kind = KindOf(r.HwType) };
                    byKey[r.GroupKey] = card;
                    cards.Add(card);
                }
                var view = new SensorView
                {
                    Id = r.Id, Name = r.Name, Value = r.Value,
                    Status = Classify(card.Kind, r.Name, r.Value),
                };
                TrackMinMax(view, newSample);
                card.Others.Add(view);
            }

            foreach (DeviceCard card in cards)
            {
                card.Primary = PickPrimary(card.Others);
                card.Others.Remove(card.Primary);
                card.History = Record(card.Primary.Id, card.Primary.Value, newSample);

                // The card's status (its pill and the window headline) follows the headline reading,
                // so they never disagree. Secondary sensors still get their own bar colours; some,
                // like the extra NVMe "Temperature #N" sensors, report values that aren't meaningful.
                card.Status = card.Primary.Status;
            }

            // Stable order: by kind, then in the order the hardware was found.
            var indexed = new List<KeyValuePair<int, DeviceCard>>();
            for (int i = 0; i < cards.Count; i++) indexed.Add(new KeyValuePair<int, DeviceCard>(i, cards[i]));
            indexed.Sort((a, b) =>
            {
                int c = a.Value.Kind.CompareTo(b.Value.Kind);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            cards.Clear();
            foreach (var kv in indexed) cards.Add(kv.Value);
            return cards;
        }

        /// <summary>Starts min/max tracking over from the next reading.</summary>
        public static void ResetMinMax()
        {
            s_minMax.Clear();
        }

        /// <summary>
        /// Min/max since the app started (or since the last reset). Non-positive samples are
        /// ignored: some drives report 0 °C on the first read.
        /// </summary>
        private static void TrackMinMax(SensorView s, bool newSample)
        {
            float[] mm;
            s_minMax.TryGetValue(s.Id, out mm);
            if (newSample && s.Value.HasValue && s.Value.Value > 0)
            {
                float v = s.Value.Value;
                if (mm == null) s_minMax[s.Id] = mm = new[] { v, v };
                else { mm[0] = Math.Min(mm[0], v); mm[1] = Math.Max(mm[1], v); }
            }
            if (mm != null) { s.Min = mm[0]; s.Max = mm[1]; }
        }

        /// <summary>
        /// Per-device thresholds: a laptop CPU at 85 °C is normal under load, while a drive at
        /// 70 °C is about to throttle, so one global limit would mislead.
        /// </summary>
        public static Status Classify(DeviceKind kind, string sensorName, float? celsius)
        {
            if (!celsius.HasValue) return Status.Unknown;
            float warm, hot;
            switch (kind)
            {
                case DeviceKind.Processor: warm = 80; hot = 90; break;
                case DeviceKind.Graphics: warm = 75; hot = 85; break;
                case DeviceKind.Drive: warm = 55; hot = 70; break;
                case DeviceKind.Memory: warm = 60; hot = 75; break;
                default: warm = 60; hot = 80; break;
            }
            // Hot-spot / junction sensors normally run ~10 °C above the core reading.
            string n = sensorName.ToLowerInvariant();
            if (n.Contains("hot spot") || n.Contains("junction")) { warm += 10; hot += 10; }

            float v = celsius.Value;
            return v >= hot ? Status.Hot : v >= warm ? Status.Warm : Status.Normal;
        }

        public static string KindLabel(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Processor: return "Processor";
                case DeviceKind.Graphics: return "Graphics card";
                case DeviceKind.Memory: return "Memory";
                case DeviceKind.Drive: return "Drive";
                case DeviceKind.Motherboard: return "Motherboard";
                default: return "Device";
            }
        }

        private static DeviceKind KindOf(HardwareType t)
        {
            switch (t)
            {
                case HardwareType.Cpu: return DeviceKind.Processor;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel: return DeviceKind.Graphics;
                case HardwareType.Memory: return DeviceKind.Memory;
                case HardwareType.Storage: return DeviceKind.Drive;
                case HardwareType.Motherboard:
                case HardwareType.SuperIO:
                case HardwareType.EmbeddedController: return DeviceKind.Motherboard;
                default: return DeviceKind.Other;
            }
        }

        private static SensorView PickPrimary(List<SensorView> sensors)
        {
            foreach (string name in PrimaryNames)
                foreach (SensorView s in sensors)
                    if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;

            SensorView hottest = sensors[0];
            foreach (SensorView s in sensors)
                if (s.Value.HasValue && (!hottest.Value.HasValue || s.Value > hottest.Value)) hottest = s;
            return hottest;
        }

        private static float[] Record(string id, float? value, bool newSample)
        {
            Queue<float> q;
            if (!s_history.TryGetValue(id, out q))
            {
                q = new Queue<float>(HistoryLength + 1);
                s_history[id] = q;
            }
            if (newSample)
            {
                q.Enqueue(value.HasValue ? value.Value : float.NaN);
                while (q.Count > HistoryLength) q.Dequeue();
            }
            return q.ToArray();
        }
    }
}
