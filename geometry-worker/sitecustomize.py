"""Configure native library lookup for the app-owned Python runtime."""

from __future__ import annotations

import os
from pathlib import Path


_dll_directory_handle = None
if os.name == "nt":
    native_directory = Path(__file__).resolve().parent / "Library" / "bin"
    if native_directory.is_dir():
        # Some native dependencies (notably the BLAS runtime) load their own
        # secondary DLLs through the process PATH rather than Python's loader.
        os.environ["PATH"] = f"{native_directory}{os.pathsep}{os.environ.get('PATH', '')}"
        # Retain the handle for the process lifetime. Closing it removes the
        # directory from the Windows DLL search path.
        _dll_directory_handle = os.add_dll_directory(native_directory)
