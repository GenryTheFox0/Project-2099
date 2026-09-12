from __future__ import annotations

import argparse
import http.server
import os
from pathlib import Path


class RangeHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, directory: str, log_path: Path, **kwargs):
        self._log_path = log_path
        super().__init__(*args, directory=directory, **kwargs)

    def log_message(self, format: str, *args) -> None:
        with self._log_path.open("a", encoding="utf-8") as stream:
            stream.write((format % args) + "\n")

    def send_head(self):
        path = self.translate_path(self.path)
        if os.path.isdir(path):
            return super().send_head()
        try:
            source = open(path, "rb")
        except OSError:
            self.send_error(404, "File not found")
            return None
        size = os.fstat(source.fileno()).st_size
        start = 0
        range_header = self.headers.get("Range")
        if range_header and range_header.startswith("bytes="):
            value = range_header[6:].split(",", 1)[0].split("-", 1)[0]
            if value:
                start = int(value)
        if start < 0 or start >= size:
            source.close()
            self.send_error(416, "Requested Range Not Satisfiable")
            return None
        self._range_start = start
        self._range_remaining = size - start
        self.send_response(206 if start else 200)
        self.send_header("Content-Type", "application/zip")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(self._range_remaining))
        if start:
            self.send_header("Content-Range", f"bytes {start}-{size - 1}/{size}")
        self.end_headers()
        source.seek(start)
        return source

    def copyfile(self, source, outputfile) -> None:
        remaining = getattr(self, "_range_remaining", None)
        if remaining is None:
            return super().copyfile(source, outputfile)
        while remaining:
            block = source.read(min(1024 * 1024, remaining))
            if not block:
                break
            outputfile.write(block)
            remaining -= len(block)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--log", required=True)
    args = parser.parse_args()
    log_path = Path(args.log)
    handler = lambda *a, **kw: RangeHandler(
        *a, directory=args.directory, log_path=log_path, **kw
    )
    with http.server.ThreadingHTTPServer(("127.0.0.1", args.port), handler) as server:
        server.serve_forever()


if __name__ == "__main__":
    main()
