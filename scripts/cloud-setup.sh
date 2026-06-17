#!/usr/bin/env bash
set -euo pipefail

dotnet_channel="${DOTNET_CHANNEL:-10.0}"
dotnet_install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
solution_path="${SOLUTION_PATH:-SSW.TimePro.Timesheets.Cli.slnx}"
run_tests=0

usage() {
    cat <<'USAGE'
Usage: scripts/cloud-setup.sh [--test]

Installs the required .NET SDK if needed, then restores the solution.

Options:
  --test    Run dotnet test after restore.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --test)
            run_tests=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1

has_required_dotnet() {
    if ! command -v dotnet >/dev/null 2>&1; then
        return 1
    fi

    dotnet --list-sdks | awk '{print $1}' | grep -Eq "^${dotnet_channel//./\\.}\\."
}

add_dotnet_to_current_environment() {
    export DOTNET_ROOT="$dotnet_install_dir"
    export PATH="$dotnet_install_dir:$PATH"
}

persist_dotnet_for_later_steps() {
    if [[ -n "${GITHUB_PATH:-}" ]]; then
        echo "$dotnet_install_dir" >> "$GITHUB_PATH"
    fi

    if [[ -n "${GITHUB_ENV:-}" ]]; then
        echo "DOTNET_ROOT=$dotnet_install_dir" >> "$GITHUB_ENV"
    fi
}

persist_dotnet_for_later_shells() {
    local dotnet_root_line="export DOTNET_ROOT=\"$dotnet_install_dir\""
    local path_line="export PATH=\"$dotnet_install_dir:\$PATH\""
    local profile_file

    for profile_file in "$HOME/.profile" "$HOME/.bashrc"; do
        touch "$profile_file"

        if ! grep -Fqx "$dotnet_root_line" "$profile_file" || ! grep -Fqx "$path_line" "$profile_file"; then
            {
                echo ""
                echo "# Added by cloud setup"
                echo "$dotnet_root_line"
                echo "$path_line"
            } >> "$profile_file"
        fi
    done
}

if ! has_required_dotnet; then
    tmp_dir="$(mktemp -d)"
    trap 'rm -rf "$tmp_dir"' EXIT

    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp_dir/dotnet-install.sh"
    bash "$tmp_dir/dotnet-install.sh" --channel "$dotnet_channel" --install-dir "$dotnet_install_dir"

    add_dotnet_to_current_environment
    persist_dotnet_for_later_steps

    if [[ "${GITHUB_ACTIONS:-}" != "true" ]]; then
        persist_dotnet_for_later_shells
    fi
fi

dotnet --info
dotnet restore "$solution_path"

if [[ "$run_tests" -eq 1 ]]; then
    dotnet test "$solution_path" --no-restore
fi
