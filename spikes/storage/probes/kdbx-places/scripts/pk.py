"""pykeepass for `kdbx-places`.

    resave <in> <out>   open and save
    edit <in> <out>     an unrelated entry added at the root, then save
"""
import sys

from pykeepass import PyKeePass


def main() -> None:
    action, src, dst = sys.argv[1:4]
    try:
        kp = PyKeePass(src, password="probe-only")
        if action == "edit":
            kp.add_entry(kp.root_group, "pk-added", "pk-user", "pk-password")
        kp.save(dst)
        print(f"{action} ok")
    except Exception as e:  # noqa: BLE001 - a refusal is a result here
        print(f"ERROR {type(e).__name__}: {str(e)[:200]}")
        sys.exit(1)


if __name__ == "__main__":
    main()
