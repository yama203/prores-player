#!/bin/bash
# Builds "ProRes Player.app": self-contained .NET plus libmpv, ffmpeg and their libraries copied in from
# Homebrew (brew install mpv ffmpeg), so the result runs on Macs without Homebrew.
#   tools/mac/make-app.sh                build + install to ~/Applications
#   tools/mac/make-app.sh --no-install   build only (dist/mac/ProRes Player.app)
#   tools/mac/make-app.sh --dmg          build + dist/mac/ProResPlayer-<version>-mac-arm64.dmg (no install)
# The bundled Homebrew libraries only run on the macOS version they were built for (or later);
# LSMinimumSystemVersion is set from libmpv's.
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
# Lowest macOS the bundled libraries accept.
MINOS=$(vtool -show-build /opt/homebrew/lib/libmpv.2.dylib | awk '/minos/ {print $2; exit}')
sed -e "s/@VERSION@/$VERSION/g" -e "s/@MINOS@/${MINOS:-12.0}/g" tools/mac/Info.plist > "$APP/Contents/Info.plist"
python3 tools/mac/bundle-libs.py "$APP"

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
if [ "${1:-}" = "--dmg" ]; then
	DMG="$OUT/ProResPlayer-$VERSION-mac-arm64.dmg"
	STAGE="$OUT/dmg"
	rm -rf "$STAGE" "$DMG" && mkdir -p "$STAGE"
	cp -R "$APP" "$STAGE/"
	ln -s /Applications "$STAGE/Applications"
	cp LICENSE "$STAGE/LICENSE.txt"
	cp tools/mac/README-mac.txt "$STAGE/はじめにお読みください.txt"
	hdiutil create -volname "ProRes Player $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG" >/dev/null
	rm -rf "$STAGE"
	echo "dmg: $DMG ($(du -h "$DMG" | cut -f1))"
	exit 0
fi
if [ "${1:-}" != "--no-install" ]; then
	mkdir -p "$HOME/Applications"
	rm -rf "$HOME/Applications/ProRes Player.app"
	cp -R "$APP" "$HOME/Applications/"
	# Register with Launch Services so Finder's "Open With" lists it right away.
	/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$HOME/Applications/ProRes Player.app" || true
	echo "installed: $HOME/Applications/ProRes Player.app"
fi
