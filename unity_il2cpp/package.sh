#!/bin/bash
publish_dumper() {
    ./publish.trimmed.sh $1 $2
    mkdir ../unity_il2cpp/.publish/$1-$2 || true
    cp bin/Release/net9.0/$1-$2/publish/* ../unity_il2cpp/.publish/$1-$2/
}

package_release() {
    cp -r ../../dist/ffmpeg-src $1/
    cp -r ../../dist/ffmpeg-$1 $1/ffmpeg
    cp -t $1 ../bin/Release/net6.0/publish/{CsvHelper,FFMpegCore,Instances,Wampfer,Wampfer.Shared}.dll
    7z a -mx9 Wampfer.IL2CPP.$1.7z $1
}

mkdir .publish || true
./publish.sh
cd ../dumper
publish_dumper win x64
publish_dumper win x86
publish_dumper linux x64
cd ../unity_il2cpp/.publish
package_release win-x64
package_release win-x86
package_release linux-x64
