from __future__ import annotations

import io
import os
import stat
import sys
import tarfile
from pathlib import Path

import zstandard


def normalized_tar(root: Path, members: list[Path], output: Path, executable_files: set[str]) -> None:
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.GNU_FORMAT) as archive:
        for member in members:
            absolute = root / member
            entries = [absolute]
            if absolute.is_dir():
                entries.extend(sorted(absolute.rglob("*")))
            for entry in entries:
                relative = entry.relative_to(root).as_posix()
                info = archive.gettarinfo(str(entry), arcname=f"./{relative}")
                info.uid = 0
                info.gid = 0
                info.uname = "root"
                info.gname = "root"
                info.mtime = 0
                if info.isdir():
                    info.mode = 0o755
                    archive.addfile(info)
                else:
                    info.mode = 0o755 if relative in executable_files else 0o644
                    with entry.open("rb") as source:
                        archive.addfile(info, source)
    compressor = zstandard.ZstdCompressor(level=19)
    output.write_bytes(compressor.compress(raw.getvalue()))


def write_ar(output: Path, items: list[tuple[str, bytes]]) -> None:
    with output.open("wb") as archive:
        archive.write(b"!<arch>\n")
        for name, data in items:
            encoded_name = (name + "/").encode("ascii")
            header = (
                encoded_name.ljust(16, b" ")
                + b"0".ljust(12, b" ")
                + b"0".ljust(6, b" ")
                + b"0".ljust(6, b" ")
                + b"100644".ljust(8, b" ")
                + str(len(data)).encode("ascii").ljust(10, b" ")
                + b"`\n"
            )
            archive.write(header)
            archive.write(data)
            if len(data) % 2:
                archive.write(b"\n")


def main() -> int:
    project = Path(__file__).resolve().parents[1]
    package = project / "source" / "linux-package"
    build = project / "build" / "deb"
    dist = project / "dist"
    build.mkdir(parents=True, exist_ok=True)
    dist.mkdir(parents=True, exist_ok=True)

    control_archive = build / "control.tar.zst"
    data_archive = build / "data.tar.zst"
    normalized_tar(
        package,
        [Path("control"), Path("postinst"), Path("postrm")],
        control_archive,
        {"postinst", "postrm"},
    )
    normalized_tar(
        package,
        [Path("opt"), Path("usr")],
        data_archive,
        {"usr/bin/md-viewer-plain", "opt/md-viewer-plain/md_viewer.py", "opt/md-viewer-plain/docx_export.py"},
    )

    output = dist / "md-viewer-plain_2.2.0-4_all.deb"
    write_ar(
        output,
        [
            ("debian-binary", b"2.0\n"),
            ("control.tar.zst", control_archive.read_bytes()),
            ("data.tar.zst", data_archive.read_bytes()),
        ],
    )
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
