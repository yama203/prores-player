#!/usr/bin/env python3
"""Copies libmpv, ffmpeg and every non-system dylib they need into a .app and points them at each other.

    tools/mac/bundle-libs.py "dist/mac/ProRes Player.app"

- libmpv and its dependencies go to Contents/Frameworks, each with install name @rpath/<name> and an
  LC_RPATH of @loader_path, so they find each other wherever the app is.
- ffmpeg (for lossless cutting) goes to Contents/MacOS/ffmpeg/ffmpeg with an rpath to Contents/Frameworks,
  sharing the same libraries.
- Writes Contents/Resources/THIRD-PARTY.txt listing what was bundled (Homebrew formula and version).
Everything modified is re-signed ad-hoc (Apple Silicon refuses to load unsigned / broken signatures).
"""
import os
import re
import shutil
import subprocess
import sys

SYSTEM = ("/usr/lib/", "/System/")


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout


def deps(path):
    """Library references of a Mach-O file (skipping its own install name)."""
    out = run("otool", "-L", path).splitlines()[1:]
    refs = [l.strip().split(" (")[0] for l in out]
    own = run("otool", "-D", path).splitlines()[1:]
    return [r for r in refs if not own or r != own[0].strip()]


def rpaths(path):
    out = run("otool", "-l", path)
    return re.findall(r"cmd LC_RPATH\n\s+cmdsize \d+\n\s+path (.+?) \(offset", out)


def resolve(ref, loader):
    """Absolute path of a reference made by `loader` (handles @rpath / @loader_path)."""
    base = os.path.dirname(os.path.realpath(loader))
    if ref.startswith("@loader_path/"):
        return os.path.realpath(os.path.join(base, ref[len("@loader_path/"):]))
    if ref.startswith("@rpath/"):
        for rp in rpaths(loader):
            rp = rp.replace("@loader_path", base)
            cand = os.path.join(rp, ref[len("@rpath/"):])
            if os.path.exists(cand):
                return os.path.realpath(cand)
        raise SystemExit(f"cannot resolve {ref} from {loader}")
    return os.path.realpath(ref)


def formula(real):
    m = re.search(r"/Cellar/([^/]+)/([^/]+)/", real)
    return f"{m.group(1)} {m.group(2)}" if m else real


def main():
    app = sys.argv[1]
    fw = os.path.join(app, "Contents", "Frameworks")
    ffdir = os.path.join(app, "Contents", "MacOS", "ffmpeg")
    os.makedirs(fw, exist_ok=True)
    os.makedirs(ffdir, exist_ok=True)

    roots = [("/opt/homebrew/lib/libmpv.2.dylib", None), (shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg", "ffmpeg")]
    copied = {}  # real source path -> bundled path
    queue = []
    for src, kind in roots:
        real = os.path.realpath(src)
        dst = os.path.join(ffdir, "ffmpeg") if kind == "ffmpeg" else os.path.join(fw, os.path.basename(src))
        copied[real] = dst
        queue.append(real)

    # Breadth-first over non-system dependencies.
    i = 0
    while i < len(queue):
        real = queue[i]
        i += 1
        for ref in deps(real):
            if ref.startswith(SYSTEM):
                continue
            dep = resolve(ref, real)
            if dep not in copied:
                copied[dep] = os.path.join(fw, os.path.basename(ref))
                queue.append(dep)

    for real, dst in copied.items():
        shutil.copy2(real, dst)
        os.chmod(dst, 0o755)

    for real, dst in copied.items():
        is_exe = dst.endswith("/ffmpeg")
        if not is_exe:
            run("install_name_tool", "-id", "@rpath/" + os.path.basename(dst), dst)
        for ref in deps(real):
            if ref.startswith(SYSTEM):
                continue
            target = copied[resolve(ref, real)]
            run("install_name_tool", "-change", ref, "@rpath/" + os.path.basename(target), dst)
        for rp in rpaths(dst):
            run("install_name_tool", "-delete_rpath", rp, dst)
        run("install_name_tool", "-add_rpath", "@executable_path/../../Frameworks" if is_exe else "@loader_path", dst)
        run("codesign", "--force", "--sign", "-", dst)

    # Nothing may still point into Homebrew.
    for dst in copied.values():
        bad = [r for r in deps(dst) if r.startswith("/opt/") or r.startswith("/usr/local/")]
        if bad:
            raise SystemExit(f"{dst} still references {bad}")

    lines = ["Bundled libraries (built by Homebrew; sources: https://github.com/Homebrew/homebrew-core, each",
             "formula names its upstream source). libmpv and ffmpeg are GPL v3 builds.", ""]
    for real, dst in sorted(copied.items(), key=lambda kv: os.path.basename(kv[1])):
        lines.append(f"{os.path.relpath(dst, app):55} {formula(real)}")
    res = os.path.join(app, "Contents", "Resources")
    os.makedirs(res, exist_ok=True)
    with open(os.path.join(res, "THIRD-PARTY.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")
    print(f"bundled {len(copied)} files")


if __name__ == "__main__":
    main()
