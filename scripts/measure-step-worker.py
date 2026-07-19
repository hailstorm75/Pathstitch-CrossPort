"""Measure the packaged STEP worker through its production framed protocol."""

from __future__ import annotations

import ctypes
import json
import os
import struct
import subprocess
import sys
import time

if os.name != "nt":
    import resource


def write_frame(stream, value):
    data = json.dumps(value, separators=(",", ":")).encode("utf-8")
    stream.write(struct.pack(">I", len(data)) + data)
    stream.flush()


def read_exact(stream, count):
    data = bytearray()
    while len(data) < count:
        chunk = stream.read(count - len(data))
        if not chunk:
            raise EOFError("Worker exited before returning a complete frame.")
        data.extend(chunk)
    return bytes(data)


def read_frame(stream):
    size = struct.unpack(">I", read_exact(stream, 4))[0]
    return json.loads(read_exact(stream, size))


def request(process, request_id, operation, payload):
    write_frame(process.stdin, {
        "id": request_id,
        "protocolVersion": 1,
        "operation": operation,
        "payload": payload,
    })
    response = read_frame(process.stdout)
    if response.get("id") != request_id or not response.get("ok"):
        raise RuntimeError(json.dumps(response, separators=(",", ":")))
    return response


def windows_peak_working_set(process):
    class Counters(ctypes.Structure):
        _fields_ = [
            ("cb", ctypes.c_ulong),
            ("PageFaultCount", ctypes.c_ulong),
            ("PeakWorkingSetSize", ctypes.c_size_t),
            ("WorkingSetSize", ctypes.c_size_t),
            ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
            ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
            ("PagefileUsage", ctypes.c_size_t),
            ("PeakPagefileUsage", ctypes.c_size_t),
        ]
    counters = Counters()
    counters.cb = ctypes.sizeof(counters)
    if not ctypes.windll.psapi.GetProcessMemoryInfo(
        int(process._handle), ctypes.byref(counters), counters.cb
    ):
        raise ctypes.WinError()
    return int(counters.PeakWorkingSetSize)


def main():
    python, module_root, fixture = sys.argv[1:4]
    environment = os.environ.copy()
    environment["PYTHONPATH"] = module_root
    environment.pop("PYTHONHOME", None)
    environment["PYTHONDONTWRITEBYTECODE"] = "1"
    started = time.perf_counter()
    process = subprocess.Popen(
        [python, "-B", "-m", "pathstitch_core.geometry_worker"],
        cwd=module_root,
        env=environment,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    )
    try:
        handshake = request(process, 1, "handshake", {})
        startup_seconds = time.perf_counter() - started
        import_started = time.perf_counter()
        imported = request(process, 2, "import", {"sourcePath": fixture})
        import_seconds = time.perf_counter() - import_started
        if os.name == "nt":
            peak_bytes = windows_peak_working_set(process)
        else:
            peak_bytes = 0
    finally:
        process.terminate()
        process.wait(timeout=10)
    if os.name != "nt":
        peak_rss = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss
        peak_bytes = int(peak_rss if sys.platform == "darwin" else peak_rss * 1024)
    topology = imported["topology"]
    print(json.dumps({
        "backend": handshake["protocol"]["backend"],
        "startupSeconds": round(startup_seconds, 6),
        "importSeconds": round(import_seconds, 6),
        "peakWorkingSetBytes": peak_bytes,
        "bodyCount": len(topology["bodies"]),
        "faceCount": sum(len(body["faces"]) for body in topology["bodies"]),
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
