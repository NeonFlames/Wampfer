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

using System.Diagnostics;
using System.Reflection;
using System.Globalization;
using CsvHelper;
using FFMpegCore;
using FFMpegVideoCodec = FFMpegCore.Enums.VideoCodec;
using FFMpegAudioCodec = FFMpegCore.Enums.AudioCodec;

namespace Wampfer;

public enum Codec {
    x264,
    x265,
    vp9,
    vp8
}

public class Shared {
    public Dictionary<string, string> converted = new();
    public static string dumperPath = $"{Path.GetDirectoryName(Assembly.GetAssembly(typeof(Shared))?.Location)}";
    
    public void LoadConverted(string path) {
        try {
            converted.Clear();
            using (StreamReader reader = new(path)) {
                using (CsvReader csv = new(reader, CultureInfo.InvariantCulture)) {
                    while (csv.Read()) {
                        try {
                            converted.Add(csv.GetField<string>(0), csv.GetField<string>(1));
                        } catch {}
                    }
                }
            }
        } catch (Exception e) {
            Console.WriteLine($"Wampfer.Shared - {e}");
        }
    }

    public void SaveConverted(string path) {
        try {
            using (StreamWriter writer = new(path)) {
                using (CsvWriter csv = new(writer, CultureInfo.InvariantCulture)) {
                    foreach (var record in converted) {
                        csv.WriteField(record.Key);
                        csv.WriteField(record.Value);
                        csv.NextRecord();
                    }
                }
            }
        } catch (Exception e) {
            Console.WriteLine($"Wampfer.Shared - {e}");
        }
    }

    public static int DumpAssets(string src, string dst, IEnumerable<string> dump, ref string log) {
        Process process = new();
        process.StartInfo = new() {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dumperPath,
            FileName = $"{dumperPath}/Wampfer.Dumper",
            Arguments = $"\"{src}\" \"{dst}\" {string.Join(" ", dump)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.Start();
        log += process.StandardOutput.ReadToEnd();
        log += process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int DumpAssets(string src, string dst, IEnumerable<string> dump) {
        Process process = new();
        process.StartInfo = new() {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dumperPath,
            FileName = $"{dumperPath}/Wampfer.Dumper",
            Arguments = $"\"{src}\" \"{dst}\" {string.Join(" ", dump)}",
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public List<string> HuntAssetBundle(string name, string path) {
        void hunt(ref List<string> ret, string path) {
            foreach (var directory in Directory.GetDirectories(path)) {
                hunt(ref ret, directory);
            }
            foreach (var file in Directory.GetFiles(path)) {
                if (Path.GetFileName(file) == name) {
                    ret.Add(file);
                }
            }
        }
        List<string> ret = new();
        hunt(ref ret, path);
        return ret;
    }

    public void SetupConvert(string tmp) {
        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = $"{Path.GetDirectoryName(Assembly.GetAssembly(typeof(Shared))?.Location)}/ffmpeg/bin", TemporaryFilesFolder = $"{tmp}/ffmpeg", UseCache = false});
    }

    public bool Convert(string src, string dst, Codec videoCodec, int crf, FFMpegCore.Enums.Speed speedPreset) {
        var task = ConvertAsync(src, dst, videoCodec, crf, speedPreset);
        task.Wait();
        return task.Result;
    }

    public Task<bool> ConvertAsync(string src, string dst, Codec videoCodec, int crf, FFMpegCore.Enums.Speed speedPreset) {
        switch (videoCodec) {
            case Codec.vp8:
                return FFMpegArguments.FromFileInput(src)
                    .OutputToFile(dst, true, options => options.WithVideoCodec("vp8").ForceFormat("webm").WithAudioCodec(FFMpegAudioCodec.LibVorbis)
                            .WithConstantRateFactor(crf).WithVideoBitrate(0).WithSpeedPreset(speedPreset))
                    .ProcessAsynchronously();
            case Codec.vp9:
                return FFMpegArguments.FromFileInput(src)
                    .OutputToFile(dst, true, options => options.WithVideoCodec("vp9").ForceFormat("webm").WithAudioCodec(FFMpegAudioCodec.LibVorbis)
                            .WithConstantRateFactor(crf).WithVideoBitrate(0).WithSpeedPreset(0).WithCustomArgument("-deadline " + (speedPreset <= FFMpegCore.Enums.Speed.Slow ? "best" : speedPreset >= FFMpegCore.Enums.Speed.VeryFast ? "realtime" : "good")))
                    .ProcessAsynchronously();
            case Codec.x265:
                return FFMpegArguments.FromFileInput(src)
                    .OutputToFile(dst, true, options => options.WithVideoCodec(FFMpegVideoCodec.LibX265).WithAudioCodec(FFMpegAudioCodec.Aac)
                            .WithConstantRateFactor(crf).WithSpeedPreset(speedPreset).ForceFormat("mp4"))
                    .ProcessAsynchronously();
            default:
                return FFMpegArguments.FromFileInput(src)
                    .OutputToFile(dst, true, options => options.WithVideoCodec(FFMpegVideoCodec.LibX264).WithAudioCodec(FFMpegAudioCodec.Aac)
                            .WithConstantRateFactor(crf).WithSpeedPreset(speedPreset).ForceFormat("mp4"))
                    .ProcessAsynchronously();
        }
    }
}
