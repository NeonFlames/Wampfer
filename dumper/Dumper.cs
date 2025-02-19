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

using AssetRipper.Assets.Bundles;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Structure;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;

namespace Wampfer;

public class Dumper {
    public static int Dump(string src, string dst, IEnumerable<string> files) {
        try {
            FileBase file = SchemeReader.LoadFile(src);
            if (file is FileContainer fileContainer) {
                fileContainer.ReadContents();
                CoreConfiguration configuration = new();
                configuration.ImportSettings.ScriptContentLevel = 0;
                configuration.ImportSettings.IgnoreStreamingAssets = true;
                GameStructure structure = GameStructure.Load([$"{Path.GetDirectoryName(AppContext.BaseDirectory)}/../../../"], configuration);
                if (structure.IsValid) {
                    GameAssetFactory assetFactory = new(structure.AssemblyManager);
                    SerializedBundle bundle = SerializedBundle.FromFileContainer(fileContainer, assetFactory);
                    foreach (var entry in bundle.FetchAssetsInHierarchy()) {
                        if (files.Contains(entry.GetBestName())) {
                            File.WriteAllBytes($"{dst}/{entry.GetBestName()}", bundle.ResolveResource($"{entry.Collection.Name}.resource").ToByteArray());
                        }
                    }
                }
            }
        } catch (Exception e) {
            Console.WriteLine($"Wampfer.Dumper - {e}");
            return 1;
        }
        return 0;
    }

    public static int Main(string[] args) {
        if (args.Length < 3) {
            Console.WriteLine("Incorrect usage");
            return 1;
        }
        return Dump(args[0], args[1], args[2..]);
    }
}
