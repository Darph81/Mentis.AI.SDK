#!/usr/bin/env bash
# Compares (check) or updates (sync) the SDK's proto copies against the Mentis.AI Manager.
#
#   scripts/protos.sh check [--ref <git-ref>] [manager-path]   exit 1 if the copies differ
#   scripts/protos.sh sync  [--ref <git-ref>] [manager-path]   overwrite the copies
#
# Without --ref the Manager's working tree is used (whatever branch is checked out there).
# With --ref (e.g. origin/main) the protos are read from that commit of the Manager repo.
# The Manager path defaults to $MENTIS_MANAGER_PATH, then ../SmartAI.Manager.
# The only allowed difference is the csharp_namespace line. admin.proto is out of scope.
set -euo pipefail

mode="${1:-}"
shift || true
ref=""
if [[ "${1:-}" == "--ref" ]]; then
    ref="${2:-}"
    shift 2 || { echo "--ref needs a git ref" >&2; exit 2; }
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manager_root="$(cd "${1:-${MENTIS_MANAGER_PATH:-$repo_root/../SmartAI.Manager}}" 2>/dev/null && pwd || true)"
proto_path="src/Mentis.AI.Contracts/Protos"
manager_protos="$manager_root/$proto_path"
sdk_protos="$repo_root/src/Mentis.AI.Sdk/Protos"
sdk_namespace='option csharp_namespace = "Mentis.AI.Sdk.Internal.Grpc";'
excluded="admin.proto"

if [[ "$mode" != "check" && "$mode" != "sync" ]]; then
    echo "usage: scripts/protos.sh check|sync [--ref <git-ref>] [manager-path]" >&2
    exit 2
fi

if [[ -z "$manager_root" || ! -d "$manager_protos" ]]; then
    echo "Manager not found. Pass its path as last argument or set MENTIS_MANAGER_PATH." >&2
    exit 2
fi

if [[ -n "$ref" ]]; then
    # Read the protos from the given commit instead of the working tree.
    snapshot="$(mktemp -d)"
    trap 'rm -rf "$snapshot"' EXIT
    if ! git -C "$manager_root" archive "$ref" "$proto_path" | tar -x -C "$snapshot"; then
        echo "Cannot read $proto_path at '$ref' in $manager_root" >&2
        exit 2
    fi
    manager_protos="$snapshot/$proto_path"
fi

# The Manager's proto with the SDK's csharp_namespace - what the SDK copy must look like.
expected() {
    sed -E "s|^option csharp_namespace = .*;\$|$sdk_namespace|" "$1"
}

if [[ -n "$ref" ]]; then
    source_label="$ref"
else
    source_label="working tree, branch $(git -C "$manager_root" branch --show-current 2>/dev/null || echo unknown)"
fi
manager_commit="$(git -C "$manager_root" log -1 --format='%h %s' ${ref:+"$ref"} 2>/dev/null || echo unknown)"
echo "Manager: $manager_root ($source_label @ $manager_commit)"

status=0

for source in "$manager_protos"/*.proto; do
    name="$(basename "$source")"
    [[ "$name" == "$excluded" ]] && continue
    target="$sdk_protos/$name"

    if [[ "$mode" == "sync" ]]; then
        expected "$source" > "$target"
        echo "synced   $name"
    elif [[ ! -f "$target" ]]; then
        echo "MISSING  $name - new proto in the Manager that the SDK does not cover"
        status=1
    elif ! diff -u --label "sdk/$name" --label "manager/$name" "$target" <(expected "$source"); then
        echo "DIFFERS  $name"
        status=1
    else
        echo "ok       $name"
    fi
done

# Copies without a counterpart in the Manager (renamed or removed there).
for target in "$sdk_protos"/*.proto; do
    name="$(basename "$target")"
    if [[ ! -f "$manager_protos/$name" ]]; then
        echo "ORPHAN   $name - no longer exists in the Manager"
        status=1
    fi
done

if [[ "$mode" == "sync" ]]; then
    echo "Done. Build now - the compiler points at every mapping that needs updating."
elif [[ $status -ne 0 ]]; then
    echo "Proto copies are out of date. Run: scripts/protos.sh sync${ref:+ --ref $ref}"
fi

exit $status
