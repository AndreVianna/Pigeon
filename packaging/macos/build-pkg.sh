#!/bin/sh
# Builds Pigeon-<version>-<rid>.pkg, which installs /usr/local/bin/pigeon, from a standalone publish:
#   dotnet publish src/Pigeon.Cli/Pigeon.Cli.csproj -c Release -r osx-arm64 -o artifacts/osx-arm64
#   packaging/macos/build-pkg.sh 0.6.0-beta.1 osx-arm64 artifacts/osx-arm64 artifacts
# Runs on macOS only (pkgbuild). The package is not signed or notarized, so Gatekeeper asks the
# person to confirm it (see docs/beta-testing.md). It registers no service.
set -eu
version=$1 rid=$2 publish=$3 out=$4
case "$rid" in
  osx-arm64) arch=arm64 ;;
  osx-x64) arch=x86_64 ;;
  *) echo "unknown macOS runtime: $rid" >&2; exit 2 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/root/usr/local/bin" "$work/scripts"
cp "$publish/pigeon" "$work/root/usr/local/bin/pigeon"
chmod 755 "$work/root/usr/local/bin/pigeon"

# Refuses to install the build for the other processor.
cat > "$work/scripts/preinstall" <<EOF
#!/bin/sh
if [ "\$(uname -m)" != "$arch" ]; then
  echo "This Pigeon package is for $arch Macs; this Mac is \$(uname -m)." >&2
  exit 1
fi
# A running service keeps the old version in memory until it is restarted.
[ -x /usr/local/bin/pigeon ] && /usr/local/bin/pigeon stop >/dev/null 2>&1 || true
exit 0
EOF
chmod 755 "$work/scripts/preinstall"

mkdir -p "$out"
pkgbuild --root "$work/root" --scripts "$work/scripts" --identifier io.github.andrevianna.pigeon \
  --version "$version" --install-location / "$out/Pigeon-$version-$rid.pkg"
