#!/usr/bin/env sh
# Packs the plugin contract into a local NuGet feed.
#
# The contract is not published to nuget.org, so we fetch the server and pack it. Since
# plugin ABI 12 it is two packages: NoMercy.PluginSdk.Abstractions, which carries
# NoMercy.Events and NoMercy.Design inside it, and NoMercy.PluginSdk.Mvc, which holds
# PluginControllerBase. The Mvc base class has its own assembly on purpose: it must keep
# one identity across the load-context boundary, and putting it in Abstractions would
# force an ASP.NET Core framework reference on every plugin.
#
# Which server: the newest release tag, because a tag is what an owner actually runs.
# `master` stopped at ABI 10 in August 2026 while releases moved on, and building against
# it produced a plugin every up-to-date server refused. SERVER_REF pins anything else -
# a tag or a commit - and a release's notes hand out that exact command.
#
# Where: _server/ inside this repository (already in .gitignore). It used to default to
# a sibling checkout of the server, which on a developer machine is their own working
# copy - and this script sparse-checks-out and hard-resets whatever it is pointed at.
# SERVER_DIR still overrides the location.

set -eu

# CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on PATH may be
# an older SDK that cannot build net10.0, and the usable one is a side-by-side install
# under the user profile. So the version decides, not the location.
can_build_net10() {
    [ -x "$1" ] || [ "$1" = dotnet ] || return 1
    "$1" --list-sdks 2>/dev/null | grep -q '^10\.'
}

for candidate in "${USERPROFILE:-}/.dotnet/dotnet.exe" "${HOME:-}/.dotnet/dotnet" dotnet; do
    if can_build_net10 "$candidate"; then
        dotnet="$candidate"
        break
    fi
done

if [ -z "${dotnet:-}" ]; then
    echo "no dotnet SDK on this machine can build net10.0" >&2
    exit 1
fi

root=$(cd "$(dirname "$0")/.." && pwd)
server="${SERVER_DIR:-$root/_server}"
feed="$root/_nupkgs"
remote=https://github.com/NoMercy-Entertainment/nomercy-media-server.git

ref="${SERVER_REF:-}"
if [ -z "$ref" ]; then
    # --refs drops the peeled ^{} lines; sort -V orders v0.1.99 before v0.1.100.
    ref=$(git ls-remote --tags --refs "$remote" 'v*' | sed 's#.*refs/tags/##' | sort -V | tail -n 1)
    if [ -z "$ref" ]; then
        echo "could not resolve the newest nomercy-media-server release tag" >&2
        exit 1
    fi
fi

if [ ! -d "$server/.git" ]; then
    mkdir -p "$server"
    git -C "$server" init -q
    git -C "$server" remote add origin "$remote"
fi

# `set`, not `add`: this checkout belongs to this repository alone now, so the list is
# exactly what this script needs and nothing another plugin left behind. Cone mode also
# keeps the root files - Directory.Build.props, Directory.Packages.props, .editorconfig,
# LICENSE - which the pack reads.
git -C "$server" sparse-checkout init --cone
git -C "$server" sparse-checkout set \
    src/NoMercy.PluginSdk.Abstractions src/NoMercy.PluginSdk.Mvc src/NoMercy.Events src/NoMercy.Design

git -C "$server" fetch --depth=1 --filter=blob:none origin "$ref"
git -C "$server" checkout -q --force FETCH_HEAD

# Emptied, not topped up: a leftover package of another version would win the floating
# reference and silently decide the contract instead of the ref asked for.
rm -rf "$feed"
mkdir -p "$feed"

for project in NoMercy.PluginSdk.Abstractions NoMercy.PluginSdk.Mvc; do
    csproj="$server/src/$project/$project.csproj"
    if [ ! -f "$csproj" ]; then
        echo "$project is not present at $ref - that ref predates plugin ABI 12" >&2
        exit 1
    fi

    # MSB9008 about a missing NoMercy.Analyzers is expected under a sparse checkout. It is
    # the server's own analyzer reference; the package builds correctly without it.
    "$dotnet" pack "$csproj" -c Release -o "$feed"
done

sha=$(git -C "$server" rev-parse HEAD)
printf '%s %s\n' "$ref" "$sha" > "$feed/CONTRACT"

find "$feed" -maxdepth 1 -name '*.nupkg' -print
echo "contract packed from nomercy-media-server $ref ($sha)"
