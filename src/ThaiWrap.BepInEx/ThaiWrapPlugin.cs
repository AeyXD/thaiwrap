using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ThaiWrap.BepInEx
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ThaiWrapPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "thaiwrap.plugin";
        public const string PluginName = "ThaiWrap";
        public const string PluginVersion = "0.4.1";

        internal static ThaiTextFilter? Filter;
        internal static ManualLogSource? Log;

        // ถูก bind ใน Awake() ตาม pattern ของ BepInEx — ไม่ใช่ใน constructor
        private ConfigEntry<bool>? _enabled;
        private ConfigEntry<int>? _maxWordLength;
        private ConfigEntry<int>? _unknownBreakThreshold;
        private string? _wordListPath;
        private int _lastMaxWordLength;
        private int _lastUnknownThreshold;

        private void Awake()
        {
            Log = Logger;

            _enabled = Config.Bind("General", "Enabled", true,
                "เปิด/ปิดการแทรก ZWSP ทั้งหมด (ไม่ต้องรีสตาร์ทเกม)");
            _maxWordLength = Config.Bind("Segmentation", "MaxWordLength", 0,
                "ความยาวคำสูงสุดจากพจนานุกรม (0 = ไม่จำกัด — แนะนำสำหรับอัลกอริทึม DP)");
            _unknownBreakThreshold = Config.Bind("Segmentation", "UnknownBreakThreshold", 8,
                "จำนวน cluster ก่อนยอมให้ตัดข้อความนอกพจนานุกรม");

            string? wordListPath = FindWordList();
            if (wordListPath == null)
            {
                Logger.LogError("ไม่พบ words_th.txt — วางไฟล์ไว้ที่ BepInEx/plugins/ThaiWrap/words_th.txt (โหลดจาก https://github.com/AeyXD/thaiwrap) แล้วรีสตาร์ทเกม");
                return;
            }

            try
            {
                var segmenter = ThaiSegmenter.FromFile(wordListPath, new ThaiWrapOptions
                {
                    MaxWordLength = _maxWordLength!.Value,
                    UnknownBreakThreshold = _unknownBreakThreshold!.Value
                });
                Filter = new ThaiTextFilter(segmenter) { Enabled = _enabled.Value };
                _wordListPath = wordListPath;
                _lastMaxWordLength = _maxWordLength.Value;
                _lastUnknownThreshold = _unknownBreakThreshold.Value;
                Logger.LogInfo($"โหลดพจนานุกรม {segmenter.WordCount} คำ จาก {wordListPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError("โหลด word list ไม่สำเร็จ: " + ex.Message);
                return;
            }

            int patched = InstallHooks();
            if (patched > 0)
                Logger.LogInfo($"ThaiWrap พร้อมทำงาน — hook สำเร็จ {patched} ตำแหน่ง (TMP/UGUI ที่พบในเกมนี้)");
            else
                Logger.LogError("หา hook ไม่ได้เลย — ไม่พบ TextMeshPro / UnityEngine.UI.Text หรือ patch ล้มเหลวทั้งหมด (ดู log ด้านบน) รายงาน issue พร้อมชื่อเกมและเวอร์ชัน Unity ได้ที่ github.com/AeyXD/thaiwrap");
        }

        private void Update()
        {
            if (Filter != null && _enabled != null && Filter.Enabled != _enabled.Value)
                Filter.Enabled = _enabled.Value;

            // config การตัดคำเปลี่ยนกลางเกม → สร้าง segmenter ใหม่ทันที
            if (_wordListPath != null && _maxWordLength != null && _unknownBreakThreshold != null &&
                (_maxWordLength.Value != _lastMaxWordLength || _unknownBreakThreshold.Value != _lastUnknownThreshold))
            {
                _lastMaxWordLength = _maxWordLength.Value;
                _lastUnknownThreshold = _unknownBreakThreshold.Value;
                try
                {
                    var segmenter = ThaiSegmenter.FromFile(_wordListPath, new ThaiWrapOptions
                    {
                        MaxWordLength = _lastMaxWordLength,
                        UnknownBreakThreshold = _lastUnknownThreshold
                    });
                    Filter = new ThaiTextFilter(segmenter) { Enabled = _enabled?.Value ?? true };
                    Log?.LogInfo($"โหลดพจนานุกรมใหม่ ({segmenter.WordCount} คำ) ตาม config ที่เปลี่ยน");
                }
                catch (Exception ex)
                {
                    Log?.LogError("โหลดใหม่ตาม config ไม่สำเร็จ (คงใช้ตัวเดิม): " + ex.Message);
                }
            }
        }

        /// <summary>ค้นหา word list: ข้าง DLL ก่อน แล้วตามโฟลเดอร์ plugins ของ BepInEx</summary>
        private static string? FindWordList()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string local = Path.Combine(pluginDir ?? "", "words_th.txt");
                if (File.Exists(local)) return local;

                string pluginsRoot = Path.Combine(Paths.BepInExRootPath, "plugins");
                string shared = Path.Combine(pluginsRoot, "words_th.txt");
                if (File.Exists(shared)) return shared;
            }
            catch { /* ปล่อยให้คืน null */ }
            return null;
        }

        /// <summary>
        /// Hook ตัว set ข้อความของ text component ที่พบในเกม (ค้นด้วยชื่อตอนรัน —
        /// ไม่ผูกกับ TMP/UGUI ตอน compile เพื่อให้ DLL เดียวใช้ได้หลายเกม)
        /// </summary>
        private static int InstallHooks()
        {
            var targets = new List<MethodBase>();
            foreach (string typeAndProp in new[]
            {
                "TMPro.TMP_Text:text",          // base ของ TMP (ถ้าไม่ abstract)
                "TMPro.TextMeshProUGUI:text",   // UI (canvas)
                "TMPro.TextMeshPro:text",       // world space
                "UnityEngine.UI.Text:text",     // UGUI เดิม
            })
            {
                string[] parts = typeAndProp.Split(':');
                var type = AccessTools.TypeByName(parts[0]);
                if (type == null) continue; // เกมไม่มี component นี้

                var prop = type.GetProperty(parts[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var setter = prop == null ? null : prop.GetSetMethod(true);
                if (setter == null || setter.IsAbstract) continue;
                if (!targets.Contains(setter)) targets.Add(setter);
            }

            var harmony = new Harmony(PluginGuid);
            var prefix = new HarmonyMethod(typeof(Hooks).GetMethod("SetTextPrefix",
                BindingFlags.Static | BindingFlags.NonPublic));
            int ok = 0;
            foreach (var method in targets)
            {
                try { harmony.Patch(method, prefix: prefix); ok++; }
                catch (Exception ex) { Log?.LogWarning($"patch {method.DeclaringType?.Name}.set_text ไม่สำเร็จ: {ex.Message}"); }
            }
            return ok; // นับเฉพาะที่ patch สำเร็จจริง
        }

        private static class Hooks
        {
            // __0 = argument แรกของ setter ไม่ว่าพารามิเตอร์จะชื่ออะไร — แก้ค่าก่อนเข้า setter จริง
            private static void SetTextPrefix(ref string __0)
            {
                var filter = Filter;
                if (filter == null || !filter.Enabled) return;
                __0 = filter.Process(__0) ?? __0;
            }
        }
    }
}
