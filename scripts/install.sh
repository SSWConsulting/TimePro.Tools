#!/usr/bin/env bash
#
# Install or update the SSW TimePro `tp` CLI from the latest GitHub Release.
#
# Usage:
#   ./scripts/install.sh
#   curl -fsSL https://raw.githubusercontent.com/SSWConsulting/TimePro.Tools/main/scripts/install.sh | bash
#
# Works on macOS and Linux. Requires the .NET 10 SDK and either curl or wget.
# Set GITHUB_TOKEN to raise the GitHub API rate limit (optional).
set -euo pipefail

REPO="SSWConsulting/TimePro.Tools"
PACKAGE_ID="SSW.TimePro.Cli"
TOOL_COMMAND="tp"
API_URL="https://api.github.com/repos/${REPO}/releases/latest"

err()  { printf '\033[31merror:\033[0m %s\n'   "$*" >&2; }
warn() { printf '\033[33mwarning:\033[0m %s\n' "$*" >&2; }
info() { printf '\033[36m==>\033[0m %s\n'      "$*"; }

# --- 1. Verify the .NET 10 SDK -------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1; then
  err "The .NET SDK was not found on your PATH."
  err "Install the .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0"
  exit 1
fi

if ! dotnet --list-sdks | grep -qE '^10\.'; then
  found="$(dotnet --list-sdks | awk '{print $1}' | paste -sd, - 2>/dev/null || true)"
  err "No .NET 10 SDK detected${found:+ (found: ${found})}."
  err "Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
  exit 1
fi

# --- 2. Downloader helpers (curl preferred, wget fallback) ---------------------
fetch() {  # fetch <url> -> stdout (GitHub API; sends auth + JSON Accept headers)
  if command -v curl >/dev/null 2>&1; then
    local args=(-fsSL -H "User-Agent: timepro-install" -H "Accept: application/vnd.github+json")
    [ -n "${GITHUB_TOKEN:-}" ] && args+=(-H "Authorization: Bearer ${GITHUB_TOKEN}")
    curl "${args[@]}" "$1"
  elif command -v wget >/dev/null 2>&1; then
    wget -qO- --header="Accept: application/vnd.github+json" "$1"
  else
    err "Neither curl nor wget is available to talk to GitHub."; exit 1
  fi
}

download() {  # download <url> <dest> (public asset; no auth header needed)
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL -o "$2" "$1"
  else
    wget -qO "$2" "$1"
  fi
}

# --- 3. Resolve the latest release's .nupkg asset ------------------------------
info "Looking up the latest release of ${REPO}…"
release_json="$(fetch "$API_URL")" || {
  err "Could not fetch the latest release from GitHub."
  err "This usually means no release has been published yet, or the API is unreachable/rate-limited."
  err "See docs/Instructions-Deployment.md for how releases are cut."
  exit 1
}

asset_url="$(printf '%s' "$release_json" \
  | grep -oE '"browser_download_url":[[:space:]]*"[^"]+\.nupkg"' \
  | sed -E 's/.*"(https[^"]+)".*/\1/' | head -n1)"

if [ -z "$asset_url" ]; then
  err "The latest release has no .nupkg asset attached."
  err "Has a non-dry-run release been published yet? See docs/Instructions-Deployment.md."
  exit 1
fi

tag="$(printf '%s' "$release_json" \
  | grep -oE '"tag_name":[[:space:]]*"[^"]+"' \
  | sed -E 's/.*"([^"]+)".*/\1/' | head -n1)"
filename="${asset_url##*/}"
version="${filename#"${PACKAGE_ID}."}"
version="${version%.nupkg}"

info "Latest release: ${tag:-unknown} (package version ${version})"

# --- 4. Download into a throwaway directory ------------------------------------
tmpdir="$(mktemp -d 2>/dev/null || mktemp -d -t timepro)"
trap 'rm -rf "$tmpdir"' EXIT
info "Downloading ${filename}…"
download "$asset_url" "$tmpdir/$filename"

# --- 5. Install or update the global tool --------------------------------------
# `dotnet tool update` installs the tool when missing and updates it otherwise,
# so this single command covers both a fresh install and a reinstall.
info "Installing ${PACKAGE_ID} ${version} as a global tool…"
dotnet tool update --global --add-source "$tmpdir" --version "$version" "$PACKAGE_ID"

# --- 6. Record installed version and check PATH --------------------------------
tools_dir="${HOME}/.dotnet/tools"
if [ -x "${tools_dir}/${TOOL_COMMAND}" ]; then
  "${tools_dir}/${TOOL_COMMAND}" --version >/dev/null 2>&1 || true
elif command -v "${TOOL_COMMAND}" >/dev/null 2>&1; then
  "${TOOL_COMMAND}" --version >/dev/null 2>&1 || true
fi

case ":${PATH}:" in
  *":${tools_dir}:"*) ;;
  *)
    warn "The .NET tools directory is not on your PATH: ${tools_dir}"
    warn "Add it to use '${TOOL_COMMAND}' directly, e.g.:"
    warn "  echo 'export PATH=\"\$PATH:${tools_dir}\"' >> ~/.zshrc  # or ~/.bashrc"
    warn "  source ~/.zshrc"
    ;;
esac

info "Installed ${PACKAGE_ID} ${version}."
info "Next: ${TOOL_COMMAND} login --tenant <id>"
