/*
    Copyright (C) 2025 NeonFlames

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.Video;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace Wampfer;

public class Patches {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(VideoPlayer), nameof(VideoPlayer.Prepare))]
    static bool VideoPlayerPreparePre(ref VideoPlayer __instance) {
        var uri = __instance.source == VideoSource.VideoClip ? __instance.clip.originalPath : __instance.url;
        if (!Plugin.shared.converted.ContainsKey(uri)) {
            string file = Path.GetFileName(uri);
            string file_name = file.Substring(0, file.LastIndexOf('.'));
            Check:
            if (Plugin.shared.converted.ContainsValue($"{file_name}{Plugin.codec_extension}")) {
                byte[] bytes = new byte[2];
                System.Random.Shared.NextBytes(bytes);
                file_name = $"{file_name}{System.Convert.ToHexString(bytes)}";
                goto Check;
            }
            string file_tmp = $"{Paths.CachePath}/Wampfer/tmp/{file}";
            string cache_path = $"{Paths.CachePath}/Wampfer/{file_name}{Plugin.codec_extension}";
            if (!File.Exists(cache_path)) {
                Plugin.Log.LogInfo($"Hunting {uri}");
                if (__instance.source == VideoSource.VideoClip) {
                    bool done = false, failed = false;
                    {
                        var test = Resources.Load<VideoClip>(uri); // TODO: needs testing
                        if (test != null) {
                            Plugin.Log.LogInfo("Found it");
                            try {
                                var raw = File.OpenRead(uri);
                                byte[] data = new byte[file.Length];
                                int toRead = file.Length, read = 0;
                                while (toRead > 0) {
                                    int n = raw.Read(data, read, toRead);
                                    if (n == 0) break;
                                    read += n;
                                    toRead -= n;
                                }
                                File.WriteAllBytes(file_tmp, data);
                                Plugin.Convert(file_tmp, cache_path, uri, file_name);
                                File.Delete(file_tmp);
                                done = true;
                            } catch {
                                Plugin.Log.LogError($"Failed to convert {uri}");
                                failed = true;
                            }
                        }
                        GameObject.Destroy(test);
                    }
                    if (!done && !failed) foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles_Native()) {
                        if (bundle.Contains(uri)) {
                            Plugin.Log.LogInfo($"Found it in {bundle.name}");
                            foreach (var bundlefile_path in Plugin.shared.HuntAssetBundle(bundle.name, Paths.GameDataPath)) {
                                try {
                                    AssetBundle bundlefile = AssetBundle.LoadFromFile(bundlefile_path);
                                    bundlefile.Unload(false);
                                } catch { // TODO: Yeah this could be improved
                                    if (Shared.DumpAssets(bundlefile_path, $"{Paths.CachePath}/Wampfer/tmp", [__instance.clip.name]) == 0) {
                                        Plugin.Convert($"{Paths.CachePath}/Wampfer/tmp/{__instance.clip.name}", cache_path, uri, file_name);
                                        File.Delete($"{Paths.CachePath}/Wampfer/tmp/{__instance.clip.name}");
                                        goto FoundClip;
                                    }
                                }
                            }
                            FoundClip:;
                        }
                    }
                } else {
                    Plugin.Convert(uri, cache_path, uri, file_name);
                }
            }
            Plugin.shared.SaveConverted($"{Paths.CachePath}/Wampfer/converted.csv");
        }
        if (Plugin.shared.converted.ContainsKey(uri)) {
            __instance.source = VideoSource.Url;
            __instance.url = $"{Paths.CachePath}/Wampfer/{Plugin.shared.converted[uri]}";
        }
        return true;
    }
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin {
    internal static new ManualLogSource Log;
    public static Shared shared;

    internal static ConfigEntry<bool> cfg_preempt;
    internal static ConfigEntry<Codec> cfg_codec;
    internal static ConfigEntry<int> cfg_crf;
    internal static ConfigEntry<FFMpegCore.Enums.Speed> cfg_speed;

    internal static string codec_extension;

    internal static void Convert(string src, string dst, string uri, string entry) {
        Log.LogInfo($"Converting {Path.GetFileName(src)}");
        shared.Convert(src, dst, cfg_codec.Value, cfg_crf.Value, cfg_speed.Value);
        shared.converted.Add(uri, entry);
    }

    internal void LoadConfig() {
        Config.SaveOnConfigSet = true;
        cfg_preempt = Config.Bind("", "Preempt", true, "Convert files ahead of time on first launch");
        cfg_codec = Config.Bind("", "Codec", Codec.vp8, "Available codecs: x264, x265, vp8, vp9");
        cfg_crf = Config.Bind("", "CRF", 25, "Constant Rate Factor for output files");
        cfg_speed = Config.Bind("", "SpeedPreset", FFMpegCore.Enums.Speed.UltraFast, "Affects file size, slower is smaller, Available presets: UltraFast, SuperFast, VeryFast, Faster, Fast, Medium, Slow, Slower, VerySlow");

        switch (cfg_codec.Value) {
            case Codec.vp8:
            case Codec.vp9:
                codec_extension = ".webm";
                break;
            case Codec.x265:
            case Codec.x264:
            default:
                codec_extension = ".mp4";
                break;
        }
    }

    public override void Load() {
        Log = base.Log;
        Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} loading");
        Directory.CreateDirectory($"{Paths.CachePath}/Wampfer/tmp/ffmpeg");
        shared = new();
        shared.LoadConverted($"{Paths.CachePath}/Wampfer/converted.csv");
        shared.SetupConvert($"{Paths.CachePath}/Wampfer/tmp");
        LoadConfig();
        if (cfg_preempt.Value) {
            Preempt();
        }
        Harmony.CreateAndPatchAll(typeof(Patches));
        Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} loaded");
    }

    internal static void Preempt() { // TODO: Handle resources as well
        Log.LogInfo("Preempt");
        void dig(ref List<(string, List<(string, string)>)> dug, string path) {
            foreach (var directory in Directory.GetDirectories(path)) {
                dig(ref dug, directory);
            }
            foreach (var file in Directory.GetFiles(path)) {
                try {
                    AssetBundle bundle = AssetBundle.LoadFromFile(file);
                    List<(string, string)> files = new();
                    foreach (var asset in bundle.LoadAllAssets(Il2CppType.From(typeof(VideoClip)))) {
                        VideoClip clip;
                        if ((clip = asset.TryCast<VideoClip>()) != null) {
                            files.Add((clip.name, clip.originalPath));
                        }
                        GameObject.DestroyImmediate(asset, true);
                    }
                    if (files.Count > 0) {
                        dug.Add((file, files));
                    }
                    bundle.Unload(true);
                } catch (Exception e) {
                    Log.LogDebug(e);
                }
            }
        }
        List<(string, List<(string, string)>)> dug = new(); // bundle, (name, originalPath)
        dig(ref dug, $"{Paths.GameDataPath}/StreamingAssets");
        foreach (var bundle in dug) {
            Log.LogDebug($"{bundle.Item1} - {string.Join(" ", bundle.Item2)}");
            List<string> convert = new(bundle.Item2.Count);
            List<((string, string), string)> convertDB = new(bundle.Item2.Count);
            foreach (var entry in bundle.Item2) {
                if (shared.converted.ContainsKey(entry.Item2)) continue;
                string out_name = entry.Item1;
                Check:;
                if (shared.converted.ContainsValue($"{out_name}{codec_extension}")) {
                    byte[] bytes = new byte[2];
                    System.Random.Shared.NextBytes(bytes);
                    out_name = $"{out_name}{System.Convert.ToHexString(bytes)}";
                    goto Check;
                }
                convert.Add(entry.Item1);
                convertDB.Add((entry, $"{out_name}{codec_extension}"));
            }
            if (convert.Count > 0) {
                try {
                    string log = "";
                    if (Shared.DumpAssets(bundle.Item1, $"{Paths.CachePath}/Wampfer/tmp", convert, ref log) == 0) {
                        foreach (var entry in convertDB) {
                            string tmp = $"{Paths.CachePath}/Wampfer/tmp/{entry.Item1.Item1}";
                            Convert(tmp, $"{Paths.CachePath}/Wampfer/{entry.Item2}", entry.Item1.Item2, entry.Item2);
                            File.Delete(tmp);
                        }
                    } else {
                        Log.LogWarning($"Failed to dump from {bundle.Item1}:\n{string.Join(' ', convertDB)}\n{string.Join(' ', convert)}\n {log}");
                    }
                } catch (Exception e) {
                    Log.LogWarning(e);
                }
            }
        }
        shared.SaveConverted($"{Paths.CachePath}/Wampfer/converted.csv");
    }
}
