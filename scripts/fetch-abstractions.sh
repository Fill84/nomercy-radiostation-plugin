#!/usr/bin/env sh
# Packs NoMercy.Plugins.Abstractions into a local NuGet feed.
#
# The contract is not published to nuget.org, so we clone the server and pack it.
# NoMercy.Events must be packed too: it is a ProjectReference of the abstractions,
# so packing only the abstractions yields a package whose dependency cannot resolve.
# NoMercy.Design is here for the same reason - the abstractions picked it up so a
# plugin can name all fifty-six design components instead of the ten it had tags
# for, and it is what carries Newtonsoft into the shared-assembly set.
#
# Every ProjectReference of NoMercy.Plugins.Abstractions has to be both in the
# sparse-checkout list AND packed into the feed. Miss the checkout and the compile
# fails on the types it cannot see; miss the pack and the plugin's own restore
# fails on a dependency the feed does not have. When the server adds one, this
# script is what needs updating - the symptom is a wall of CS0246 for a namespace
# nobody in this repository has ever referenced.
#
# NoMercy.Plugins.Mvc is deliberately NOT packed - see the plan's Task 1.

set -eu

# CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on PATH may be
# an older SDK that cannot build net10.0, and the usable one is a side-by-side install
# under the user profile - but it can just as easily be the other way round, and a
# side-by-side install without a 10.x SDK fails global.json resolution before it packs
# anything. So the version decides, not the location.
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
# The server checkout lives beside this repo, not inside it: it is a checkout of another
# project and a sibling is where a developer expects to find one. One sibling also serves
# every plugin in this workspace, instead of each keeping its own copy. SERVER_DIR
# overrides the location, which is how CI keeps the clone inside its own disposable
# workspace.
server="${SERVER_DIR:-$(dirname "$root")/nomercy-media-server}"
feed="$root/_nupkgs"
# A release must be rebuildable. SERVER_REF pins the contract to one commit; it
# defaults to a branch for day-to-day work, but CI sets it to a SHA for a tag
# build so the artifact is reproducible instead of "whatever dev happened to be".
ref="${SERVER_REF:-${SERVER_BRANCH:-dev}}"

if [ ! -d "$server" ]; then
    git clone --depth=1 --branch="${SERVER_BRANCH:-dev}" --filter=blob:none --no-checkout \
        https://github.com/NoMercy-Entertainment/nomercy-media-server.git "$server"
fi

# A checkout that already exists may predate sparse-checkout, and `add` below refuses to
# run on one that was never initialised.
git -C "$server" sparse-checkout list >/dev/null 2>&1 \
    || git -C "$server" sparse-checkout init --cone

# Applied on every run, not only on the initial clone: setting it once means adding
# a project to the list silently does nothing on a checkout that already exists.
#
# `add`, not `set`. This sibling serves every plugin in the workspace and each needs a
# different slice of it - the torrent plugin also materialises NoMercy.Plugins.Mvc, for
# the REST base class this plugin has no use for. `set` replaces the whole list, so
# whichever plugin packed last would strip the others' projects back out, and the next
# build in that other repo would fail on a path its own script had already asked for.
git -C "$server" sparse-checkout add \
    src/NoMercy.Plugins.Abstractions src/NoMercy.Events src/NoMercy.Design

git -C "$server" fetch --depth=1 origin "$ref"
git -C "$server" reset --hard FETCH_HEAD

mkdir -p "$feed"

abstractions="$server/src/NoMercy.Plugins.Abstractions/NoMercy.Plugins.Abstractions.csproj"
if [ ! -f "$abstractions" ]; then
    echo "NoMercy.Plugins.Abstractions is not present at $ref - nothing to build against" >&2
    exit 1
fi

# Dependency order, and each one only if the ref actually has it. SERVER_REF pins
# this script to any commit and a release's notes hand out that exact command, so
# it has to keep working on a ref from before a project existed: NoMercy.Design is
# not in the tree at all at 886a8b3, the commit v1.0.2 was built from. Packing it
# unconditionally would break the reproduction path for every release so far.
#
# MSB9008 about a missing NoMercy.Analyzers is expected under a sparse checkout.
# It is an analyzer reference; the package builds correctly without it.
for project in NoMercy.Events NoMercy.Design NoMercy.Plugins.Abstractions; do
    csproj="$server/src/$project/$project.csproj"
    if [ ! -f "$csproj" ]; then
        echo "skipping $project - not in the tree at $ref"
        continue
    fi
    "$dotnet" pack "$csproj" -c Release -o "$feed"
done

find "$feed" -maxdepth 1 -name '*.nupkg' -print

echo "contract packed from nomercy-media-server $(git -C "$server" rev-parse HEAD)"
