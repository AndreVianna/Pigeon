#!/bin/sh
# Builds a .deb, an .rpm and a .tar.gz of Pigeon from a standalone publish; the packages install
# /usr/bin/pigeon and register no service:
#   dotnet publish src/Pigeon.Cli/Pigeon.Cli.csproj -c Release -r linux-x64 -o artifacts/linux-x64
#   packaging/linux/build-packages.sh 0.6.0-beta.1 linux-x64 artifacts/linux-x64 artifacts
# Needs dpkg-deb and rpmbuild (on Ubuntu: apt-get install rpm).
set -eu
version=$1 rid=$2 publish=$3 out=$4
case "$rid" in
  linux-x64) deb_arch=amd64 rpm_arch=x86_64 ;;
  linux-arm64) deb_arch=arm64 rpm_arch=aarch64 ;;
  *) echo "unknown Linux runtime: $rid" >&2; exit 2 ;;
esac
# 0.6.0-beta.1: Debian sorts "~" before the release (0.6.0~beta.1), RPM puts it in the release field.
base=${version%%-*}
pre=$(printf '%s' "$version" | sed -n 's/^[^-]*-//p')
deb_version=$base${pre:+~$pre}
rpm_release=${pre:+0.$pre}
rpm_release=${rpm_release:-1}
root=$(cd "$(dirname "$0")/../.." && pwd)
summary="Private conversations between AI agent sessions"
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# .tar.gz: the program and its license.
mkdir -p "$work/tar/pigeon-$version"
cp "$publish/pigeon" "$root/LICENSE" "$work/tar/pigeon-$version/"
chmod 755 "$work/tar/pigeon-$version/pigeon"
tar -C "$work/tar" -czf "$out/pigeon-$version-$rid.tar.gz" "pigeon-$version"

# .deb. Pigeon loads the system's SQLite and OpenSSL; zenity shows its approval windows.
mkdir -p "$work/deb/DEBIAN" "$work/deb/usr/bin" "$work/deb/usr/share/doc/pigeon"
install -m 755 "$publish/pigeon" "$work/deb/usr/bin/pigeon"
install -m 644 "$root/LICENSE" "$work/deb/usr/share/doc/pigeon/copyright"
cat > "$work/deb/DEBIAN/control" <<EOF
Package: pigeon
Version: $deb_version
Architecture: $deb_arch
Maintainer: Andre Vianna <noreply@github.com>
Depends: libc6, libsqlite3-0, libssl3 | libssl3t64 | libssl1.1
Recommends: zenity | kdialog
Section: utils
Priority: optional
Homepage: https://github.com/AndreVianna/Pigeon
Description: $summary
 Pigeon lets two AI agent sessions, on this computer or on another machine of
 the same network, talk to each other in private topics.
EOF
dpkg-deb --root-owner-group --build "$work/deb" "$out/pigeon_${deb_version}_$deb_arch.deb"

# .rpm, from the same program. A single-file program must not be stripped: stripping cuts off
# the bundle appended to it.
mkdir -p "$work/rpm/SOURCES" "$work/rpm/SPECS"
cp "$publish/pigeon" "$root/LICENSE" "$work/rpm/SOURCES/"
cat > "$work/rpm/SPECS/pigeon.spec" <<EOF
%global debug_package %{nil}
%global __strip /bin/true
%global _build_id_links none
Name: pigeon
Version: $base
Release: $rpm_release
Summary: $summary
License: MIT
URL: https://github.com/AndreVianna/Pigeon
Requires: sqlite-libs, openssl-libs
Recommends: zenity

%description
Pigeon lets two AI agent sessions, on this computer or on another machine of the
same network, talk to each other in private topics.

%install
install -D -m 755 %{_sourcedir}/pigeon %{buildroot}%{_bindir}/pigeon
install -D -m 644 %{_sourcedir}/LICENSE %{buildroot}%{_licensedir}/pigeon/LICENSE

%files
%{_bindir}/pigeon
%license %{_licensedir}/pigeon/LICENSE
EOF
rpmbuild --quiet -bb --target "$rpm_arch" --define "_topdir $work/rpm" "$work/rpm/SPECS/pigeon.spec"
cp "$work"/rpm/RPMS/*/*.rpm "$out/"
