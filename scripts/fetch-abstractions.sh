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

# CI puts the right SDK on PATH. On a Windows dev machine the `dotnet` on PATH is
# an older SDK that cannot build net10.0, and the usable one is a side-by-side
# install under the user profile, so prefer that when it is there.
if [ -x "${USERPROFILE:-}/.dotnet/dotnet.exe" ]; then
    dotnet="${USERPROFILE}/.dotnet/dotnet.exe"
elif [ -x "${HOME:-}/.dotnet/dotnet" ]; then
    dotnet="${HOME}/.dotnet/dotnet"
else
    dotnet=dotnet
fi

root=$(cd "$(dirname "$0")/.." && pwd)
server="$root/_server"
feed="$root/_nupkgs"
# A release must be rebuildable. SERVER_REF pins the contract to one commit; it
# defaults to a branch for day-to-day work, but CI sets it to a SHA for a tag
# build so the artifact is reproducible instead of "whatever dev happened to be".
ref="${SERVER_REF:-${SERVER_BRANCH:-dev}}"

if [ ! -d "$server" ]; then
    git clone --depth=1 --branch="${SERVER_BRANCH:-dev}" --filter=blob:none --no-checkout \
        https://github.com/NoMercy-Entertainment/nomercy-media-server.git "$server"
    git -C "$server" sparse-checkout init --cone
fi

# Applied on every run, not only on the initial clone: setting it once means adding
# a project to the list silently does nothing on a checkout that already exists.
git -C "$server" sparse-checkout set \
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
