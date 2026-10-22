#!/bin/zsh
# Builds "ChordPro Viewer.app" plus a .zip and .dmg in artifacts/.
#
#   scripts/package-mac.sh          # for this Mac's architecture
#   scripts/package-mac.sh x64      # for Intel Macs
#
# The app is self-contained (no .NET install needed) and ad-hoc signed, which is enough to run it on
# this Mac. To give it to other people without Gatekeeper warnings it needs signing with an Apple
# Developer ID and notarizing; see the README.
set -euo pipefail

arch=${1:-$([[ $(uname -m) == arm64 ]] && echo arm64 || echo x64)}
[[ $arch == arm64 || $arch == x64 ]] || { echo "usage: $0 [arm64|x64]" >&2; exit 1; }

root=${0:A:h:h}
project=$root/src/ChordPro.Viewer/ChordPro.Viewer.csproj
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $project)
out=$root/artifacts
publish=$out/publish-$arch
app="$out/ChordPro Viewer.app"
name=ChordPro-Viewer-$version-$arch

echo "Packaging ChordPro Viewer $version for $arch"
rm -rf $publish "$app" $out/AppIcon.iconset $out/$name.zip $out/$name.dmg $out/dmg

dotnet publish $project -c Release -r osx-$arch --self-contained -p:PublishReadyToRun=true -o $publish

# Bundle layout: Contents/{Info.plist, MacOS/<everything published>, Resources/AppIcon.icns}
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R $publish/. "$app/Contents/MacOS/"
# Whisper.net ships native libraries for every platform; keep only this Mac's.
find "$app/Contents/MacOS/runtimes" -mindepth 1 -maxdepth 1 -type d ! -name "macos-$arch" -exec rm -rf {} + 2>/dev/null || true
sed "s/__VERSION__/$version/g" $root/packaging/macos/Info.plist > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" >/dev/null

# Icon: every size macOS asks for, from the one 1024-pixel source.
iconset=$out/AppIcon.iconset
mkdir -p $iconset
for size in 16 32 128 256 512; do
    sips -z $size $size $root/src/ChordPro.Viewer/Assets/AppIcon.png --out $iconset/icon_${size}x${size}.png >/dev/null
    sips -z $((size * 2)) $((size * 2)) $root/src/ChordPro.Viewer/Assets/AppIcon.png --out $iconset/icon_${size}x${size}@2x.png >/dev/null
done
iconutil -c icns $iconset -o "$app/Contents/Resources/AppIcon.icns"
rm -rf $iconset

# Ad-hoc signature. macOS treats everything in Contents/MacOS as code, including .NET's .dll files,
# so sign each file there first, then the bundle as a whole.
find "$app/Contents/MacOS" -type f -exec codesign --force --sign - {} \; 2>/dev/null
codesign --force --sign - "$app"
codesign --verify --strict "$app"

# Distributables: a zip, and a disk image with a shortcut to Applications.
ditto -c -k --keepParent "$app" $out/$name.zip
mkdir -p $out/dmg
cp -R "$app" $out/dmg/
ln -s /Applications $out/dmg/Applications
hdiutil create -quiet -volname "ChordPro Viewer" -srcfolder $out/dmg -ov -format UDZO $out/$name.dmg
rm -rf $out/dmg $publish

echo
echo "Built:"
echo "  $app"
echo "  $out/$name.zip"
echo "  $out/$name.dmg"
