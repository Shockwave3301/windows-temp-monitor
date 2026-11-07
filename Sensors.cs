using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace TempMonitor
{
    /// <summary>One temperature reading, copied off the worker thread for the UI.</summary>
    internal sealed class Reading
    {
        public string Id;          // unique sensor identifier, e.g. "/intelcpu/0/temperature/1"
        public string GroupKey;    // hardware identifier
        public string GroupName;   // e.g. "Intel Core i7-12700H"
        public HardwareType HwType;
        public string Name;        // e.g. "CPU Package"
        public float? Value, Min, Max;
    }

    /// <summary>Thin wrapper around LibreHardwareMonitorLib for temperature sensors.</summary>
    internal static class Sensors
    {
        public static Computer CreateComputer()
        {
            return new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsMemoryEnabled = true,
                IsControllerEnabled = true,
            };
        }

        /// <summary>Updates every piece of hardware and returns all temperature sensors.</summary>
        public static List<Reading> ReadAll(Computer computer)
        {
            var readings = new List<Reading>();
            foreach (IHardware hw in computer.Hardware)
                Collect(hw, null, readings);
            return readings;
        }

        public static void ResetMinMax(Computer computer)
        {
            foreach (IHardware hw in computer.Hardware)
                ResetMinMax(hw);
        }

        private static void ResetMinMax(IHardware hw)
        {
            foreach (ISensor s in hw.Sensors)
            {
                s.ResetMin();
                s.ResetMax();
            }
            foreach (IHardware sub in hw.SubHardware)
                ResetMinMax(sub);
        }

        private static void Collect(IHardware hw, IHardware parent, List<Reading> output)
        {
            try { hw.Update(); } catch { return; }

            string groupName = parent == null ? hw.Name : parent.Name + " › " + hw.Name;
            HardwareType type = parent == null ? hw.HardwareType : parent.HardwareType;

            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || !IsLiveReading(s.Name)) continue;
                output.Add(new Reading
                {
                    Id = s.Identifier.ToString(),
                    GroupKey = hw.Identifier.ToString(),
                    GroupName = groupName,
                    HwType = type,
                    Name = s.Name,
                    Value = s.Value,
                    Min = s.Min,
                    Max = s.Max,
                });
            }

            foreach (IHardware sub in hw.SubHardware)
                Collect(sub, hw, output);
        }

        /// <summary>
        /// Skips "temperature" sensors that are not actual readings: drive warning/critical
        /// thresholds, and CPU "Distance to TjMax" (headroom, where higher means cooler).
        /// </summary>
        private static bool IsLiveReading(string name)
        {
            return !(name.EndsWith("Distance to TjMax", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("Warning Temperature", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("Critical Temperature", StringComparison.OrdinalIgnoreCase));
        }
    }
}
