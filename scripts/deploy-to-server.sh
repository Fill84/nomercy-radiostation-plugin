#!/usr/bin/env sh
# SPDX-License-Identifier: MIT
#
# Copies a built plugin onto a running NoMercy server over ssh.
#
#   scripts/deploy-to-server.sh                 # deploy what is already built
#   scripts/deploy-to-server.sh --build         # build Release first, then deploy
#   scripts/deploy-to-server.sh --dry-run        # print the plan, touch nothing
#   SERVER=beast-unit scripts/deploy-to-server.sh
#
# THE SERVER MUST BE STOPPED FIRST. A loaded plugin's assembly is held open by
# the host, so the copy fails with "Device or resource busy" and the old build
# stays in place - which looks exactly like a deploy that worked and changed
# nothing. This script refuses to report success in that case; see the hash
# check after each file.
#
# Files go over as base64 through ssh rather than with scp. Plain scp fails
# against this host because OpenSSH 9 made it speak SFTP by default and the host
# does not answer that; `scp -O` works and is faster. This stays on base64
# because it verifies every file's hash afterwards either way, and at a few
# hundred kilobytes the difference is not worth a second code path.
set -eu

server="${SERVER:-beast-unit}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="NoMercy.Plugin.InternetRadio"
configuration="${CONFIGURATION:-Release}"
framework="${FRAMEWORK:-net10.0}"

# Where the host looks for plugins. Expanded on the far side, not here: it is
# that machine's profile, not this one's.
remote_dir="\$LOCALAPPDATA/NoMercy/plugins/$project"

out="$root/src/$project/bin/$configuration/$framework"

# Everything the host reads. The manifest travels with the assembly on
# purpose: the two carry the version independently, and a build that updates
# one without the other leaves every server reporting a version it is not
# running. There is no Core.dll in this plugin, unlike its sibling.
#
# README.md and LICENSE are deliberately not copied. The host never reads
# them, and keeping the deploy to exactly what the host loads is what makes
# the hash check below meaningful - a file the host ignores could differ
# without anyone caring, which defeats the point of checking at all.
files="$project.dll $project.deps.json plugin.json"

build=0
dry_run=0
for arg in "$@"; do
    case "$arg" in
        --build) build=1 ;;
        --dry-run) dry_run=1 ;;
        *) echo "unknown argument: $arg" >&2; exit 1 ;;
    esac
done

if [ "$build" -eq 1 ]; then
    # CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on
    # PATH may be an older SDK that cannot build net10.0 (plain `dotnet` here
    # is SDK 8), and the usable one is a side-by-side install under the user
    # profile. So the version decides, not the location - same selection as
    # scripts/fetch-abstractions.sh.
    can_build_net10() {
        [ -x "$1" ] || [ "$1" = dotnet ] || return 1
        "$1" --list-sdks 2>/dev/null | grep -q '^10\.'
    }

    dotnet=""
    for candidate in "${USERPROFILE:-}/.dotnet/dotnet.exe" "${HOME:-}/.dotnet/dotnet" dotnet; do
        if can_build_net10 "$candidate"; then
            dotnet="$candidate"
            break
        fi
    done
    [ -n "$dotnet" ] || { echo "no dotnet SDK on this machine can build net10.0" >&2; exit 1; }

    echo "building $configuration…"
    "$dotnet" build "$root/src/$project/$project.csproj" -c "$configuration" --nologo
fi

[ -d "$out" ] || { echo "nothing built at $out - run with --build" >&2; exit 1; }

# A typo'd SERVER must not silently create a stray folder on some unrelated
# box - the host only ever loads plugins from a folder it already knows
# about, so an absent one means the wrong host, not a fresh install.
if ! ssh -o BatchMode=yes "$server" "[ -d \"$remote_dir\" ]" 2>/dev/null; then
    echo "remote plugins folder does not exist: $server:$remote_dir" >&2
    echo "  is SERVER right, and is NoMercy installed there?" >&2
    exit 1
fi

lang_dir="$out/lang"

if [ "$dry_run" -eq 1 ]; then
    echo "dry run - $server:$remote_dir exists, nothing will be written"
    for file in $files; do
        if [ -f "$out/$file" ]; then
            echo "  would copy $file -> $remote_dir/$file"
        else
            echo "  would skip $file (not built)"
        fi
    done
    if [ -d "$lang_dir" ]; then
        for file in "$lang_dir"/*.json; do
            [ -f "$file" ] || continue
            echo "  would copy lang/$(basename "$file") -> $remote_dir/lang/$(basename "$file")"
        done
    else
        echo "  would skip lang/*.json (not built)"
    fi
    exit 0
fi

echo "deploying to $server…"

copy_one() {
    # $1 = local path, $2 = remote path
    local_path="$1"
    remote_path="$2"

    local_sum="$(md5sum "$local_path" | cut -d' ' -f1)"

    base64 -w0 "$local_path" > "$root/.deploy.b64"
    ssh -o BatchMode=yes "$server" "cat > /tmp/nm-deploy.b64" < "$root/.deploy.b64"
    rm -f "$root/.deploy.b64"

    remote_sum="$(
        ssh -o BatchMode=yes "$server" \
            "base64 -d /tmp/nm-deploy.b64 > \"$remote_path\" && md5sum \"$remote_path\"" \
            2>/dev/null | tr -d '\\\\*' | cut -d' ' -f1
    )"

    # The hashes are the whole point. A busy file leaves the old bytes in
    # place and the copy still "succeeds" from the shell's point of view, so
    # comparing is the only way to know a deploy actually happened.
    if [ "$local_sum" != "$remote_sum" ]; then
        echo "  FAILED $remote_path" >&2
        echo "    local  $local_sum" >&2
        echo "    remote ${remote_sum:-<none>}" >&2
        echo "    Is the server still running? A loaded plugin's dll cannot be replaced." >&2
        exit 1
    fi

    echo "  ok $(basename "$remote_path")  $local_sum"
}

for file in $files; do
    [ -f "$out/$file" ] || { echo "  skip $file (not built)"; continue; }
    copy_one "$out/$file" "$remote_dir/$file"
done

if [ -d "$lang_dir" ]; then
    ssh -o BatchMode=yes "$server" "mkdir -p \"$remote_dir/lang\""
    for file in "$lang_dir"/*.json; do
        [ -f "$file" ] || continue
        copy_one "$file" "$remote_dir/lang/$(basename "$file")"
    done
else
    echo "  skip lang/*.json (not built)"
fi

deployed_version="$(
    ssh -o BatchMode=yes "$server" \
        "grep -o '\"version\"[^,}]*' \"$remote_dir/plugin.json\"" 2>/dev/null | cut -d'"' -f4
)"

echo
echo "done - deployed version: ${deployed_version:-<unknown>}"
echo "start the server again."
