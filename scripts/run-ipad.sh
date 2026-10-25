#!/bin/zsh
# Builds ChordPro Live for a real iPad or iPhone and installs it over USB or Wi-Fi.
#
#   scripts/run-ipad.sh             # the only connected device
#   scripts/run-ipad.sh "Dennis's iPad"
#
# Signing uses a free Apple ID ("Personal Team"), set up once in Xcode; see the README. Apps signed that
# way stop opening after 7 days: run this again to reinstall (songs, settings and gigs are kept).
set -euo pipefail

root=${0:A:h:h}
project=$root/src/ChordPro.Mobile/ChordPro.Mobile.csproj
app=$root/src/ChordPro.Mobile/bin/Release/net10.0-ios/ios-arm64/ChordPro.Mobile.app

# Real devices only (simulators are listed too). Prints "identifier<TAB>name" per device.
list=$(mktemp)
xcrun devicectl list devices --json-output $list >/dev/null
devices=("${(@f)$(python3 -c '
import json, sys
for d in json.load(open(sys.argv[1]))["result"]["devices"]:
    name = d["deviceProperties"]["name"]
    if d["hardwareProperties"].get("reality") != "simulated" and sys.argv[2] in ("", name):
        print(d["identifier"] + "\t" + name)
' $list "${1:-}")}")
rm -f $list
devices=(${devices:#})
if [[ ${#devices} -ne 1 ]]; then
    echo "Connect one iPad or iPhone (unlocked, with Developer Mode on), or name it: $0 \"Device name\"" >&2
    exit 1
fi
device=${devices[1]%%$'\t'*}
echo "Installing on ${devices[1]#*$'\t'}"

# The Apple Development certificate Xcode made for the Personal Team; the build finds the matching
# provisioning profile by the bundle ID.
identity=$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Development: .*\)"/\1/p' | head -1)
[[ -n $identity ]] || { echo "No Apple Development certificate; do the one-time Xcode set-up in the README." >&2; exit 1; }

dotnet build $project -c Release -r ios-arm64 -p:CodesignKey="$identity"
xcrun devicectl device install app --device $device $app
xcrun devicectl device process launch --device $device com.cranberrysoftware.chordpro-live || true
