#!/bin/bash
# Builds "ProRes Player.app" (self-contained .NET, no runtime install needed) and copies it to ~/Applications.
#   tools/mac/make-app.sh            build + install to ~/Applications
#   tools/mac/make-app.sh --no-install   build only (dist/mac/ProRes Player.app)
# libmpv and ffmpeg come from Homebrew for now (brew install mpv ffmpeg); bundling them is a later step.
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT=$PWD
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/ProResPlayer.Mac/ProResPlayer.Mac.csproj)
OUT=dist/mac
APP="$OUT/ProRes Player.app"
PUB="$OUT/publish"

rm -rf "$PUB" "$APP"
dotnet publish src/ProResPlayer.Mac -c Release -r osx-arm64 --self-contained true -o "$PUB" -v q -nologo

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB/." "$APP/Contents/MacOS/"
sed "s/@VERSION@/$VERSION/g" tools/mac/Info.plist > "$APP/Contents/Info.plist"

# Icon: assets/app_256.png -> .icns
ICONSET="$OUT/app.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for s in 16 32 128 256; do
	sips -z $s $s assets/app_256.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
	d=$((s * 2)); [ $d -le 256 ] && sips -z $d $d assets/app_256.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/app.icns"
rm -rf "$ICONSET"

# Ad-hoc signature so Apple Silicon runs it (not notarized: other Macs need right-click > Open the first time).
codesign --force --deep --sign - "$APP" >/dev/null 2>&1

echo "built: $APP"
if [ "${1:-}" != "--no-install" ]; then
	mkdir -p "$HOME/Applications"
	rm -rf "$HOME/Applications/ProRes Player.app"
	cp -R "$APP" "$HOME/Applications/"
	# Register with Launch Services so Finder's "Open With" lists it right away.
	/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$HOME/Applications/ProRes Player.app" || true
	echo "installed: $HOME/Applications/ProRes Player.app"
fi
