# docs/api/Aver.Fabric.md, generated from source by the Aver Engine's C# API indexer (scripts/csharp-api-index.py in the engine checkout this
# repository builds against), pointed at this repository's Aver.Fabric:
#
#   python -I scripts/api-index.py --engine <engine checkout> --write    # regenerate the page
#   python -I scripts/api-index.py --engine <engine checkout> --check    # exit 1 when it is missing or stale
#
# --engine defaults to the AVER_ENGINE_DIR environment variable.
import argparse
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAGE = "Aver.Fabric.md"


def load_indexer(engine):
    path = os.path.join(engine, "scripts", "csharp-api-index.py")
    if not os.path.isfile(path):
        sys.exit("no indexer at %s: pass --engine <Aver Engine checkout>" % path)
    spec = importlib.util.spec_from_file_location("aver_api_index", path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    m.ROOT = HERE
    m.CSHARP = os.path.join(HERE, "managed")
    m.DOCS_API = os.path.join(HERE, "docs", "api")
    m.ASSEMBLIES = ["Aver.Fabric"]
    return m


def main():
    ap = argparse.ArgumentParser(description="Index Aver.Fabric's public C# surface into docs/api/Aver.Fabric.md.")
    ap.add_argument("--engine", default=os.environ.get("AVER_ENGINE_DIR", ""), help="the Aver Engine checkout whose indexer is used")
    ap.add_argument("--check", action="store_true", help="exit 1 when the page is missing or stale")
    ap.add_argument("--write", action="store_true", help="regenerate the page")
    args = ap.parse_args()
    if not (args.check or args.write) or not args.engine:
        ap.print_help()
        return 0 if not args.engine and not (args.check or args.write) else 2

    m = load_indexer(os.path.abspath(args.engine))
    text = m.build_pages()[PAGE]
    target = os.path.join(m.DOCS_API, PAGE)
    if args.write:
        os.makedirs(m.DOCS_API, exist_ok=True)
        with open(target, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print("wrote docs/api/%s" % PAGE)
        return 0
    have = m.read(target) if os.path.isfile(target) else None
    if have != text:
        print("FAIL: docs/api/%s is %s. Run: python -I scripts/api-index.py --engine <engine> --write" % (PAGE, "missing" if have is None else "stale"))
        return 1
    print("Aver.Fabric API page ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
