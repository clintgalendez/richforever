"""Show addon strip images in a window so the companion can be tested without WoW.

Usage (from the repository root, after generating fixtures with the addon tests):
    python scripts/strip_demo.py tests/fixtures/world.ppm tests/fixtures/dungeon.ppm --interval 30

The window shows each image in turn at 1:1 physical pixels. Point the companion at it by
setting, in %APPDATA%\\RichForever\\settings.json:
    "GameProcessNames": ["python"], "RequireForeverFolder": false
Close the window to simulate the game exiting.
"""

import argparse
import ctypes
import sys
import tkinter as tk


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("images", nargs="+", help="PPM strip images (tests/fixtures/*.ppm)")
    parser.add_argument("--interval", type=float, default=20, help="seconds per image (default 20)")
    parser.add_argument("--x", type=int, default=200)
    parser.add_argument("--y", type=int, default=200)
    args = parser.parse_args()

    if sys.platform == "win32":
        ctypes.windll.shcore.SetProcessDpiAwareness(2)  # physical pixels, like WoW

    root = tk.Tk()
    root.title("RichForever strip demo")
    root.geometry(f"+{args.x}+{args.y}")
    root.configure(background="#202020")
    root.attributes("-topmost", True)  # the companion only reads the strip when nothing covers it
    images = [tk.PhotoImage(file=path) for path in args.images]
    label = tk.Label(root, image=images[0], borderwidth=0, highlightthickness=0)
    label.pack(anchor="nw")
    caption = tk.Label(root, text=args.images[0], fg="#cccccc", bg="#202020")
    caption.pack(anchor="w", padx=4, pady=4)

    index = [0]

    def advance():
        index[0] = (index[0] + 1) % len(images)
        label.configure(image=images[index[0]])
        caption.configure(text=args.images[index[0]])
        root.after(int(args.interval * 1000), advance)

    if len(images) > 1:
        root.after(int(args.interval * 1000), advance)
    root.mainloop()


if __name__ == "__main__":
    main()
