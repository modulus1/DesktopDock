#!/usr/bin/env python3
"""Start DesktopDock. Run with pythonw.exe (or double-click) for no console window."""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from desktopdock.app import main

if __name__ == "__main__":
    main()
