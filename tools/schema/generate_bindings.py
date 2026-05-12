#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import json
import re


ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "schemas" / "codegen.json"
MESSAGE_RE = re.compile(r"message\s+(\w+)\s*\{")


def messages(proto_text: str) -> list[str]:
    return MESSAGE_RE.findall(proto_text)


def write_python(out: Path, names: list[str]) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    body = ["from dataclasses import dataclass\n\n"]
    for name in names:
        body.append("@dataclass\n")
        body.append(f"class {name}:\n")
        body.append("    pass\n\n")
    out.write_text("".join(body), encoding="utf-8")


def write_cpp(out: Path, names: list[str], namespace: str) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    guard = re.sub(r"[^A-Z0-9]", "_", str(out.relative_to(ROOT)).upper())
    body = [f"#ifndef {guard}\n#define {guard}\n\nnamespace {namespace} {{\n"]
    for name in names:
        body.append(f"struct {name} {{}};\n")
    body.append(f"}}  // namespace {namespace}\n\n#endif\n")
    out.write_text("".join(body), encoding="utf-8")


def write_csharp(out: Path, names: list[str], namespace: str) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    body = [f"namespace {namespace};\n\n"]
    for name in names:
        body.append(f"public sealed class {name} {{}}\n")
    out.write_text("".join(body), encoding="utf-8")


def main() -> int:
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    for root in config["schema_roots"]:
        for proto in sorted((ROOT / root).glob("*.proto")):
            rel = proto.relative_to(ROOT / "schemas")
            names = messages(proto.read_text(encoding="utf-8"))
            stem = proto.stem + "_pb"
            write_python(ROOT / "generated" / "python" / rel.parent / f"{stem}.py", names)
            cpp_namespace = "sts2::generated::" + "::".join(rel.parent.parts)
            write_cpp(ROOT / "generated" / "cpp" / rel.parent / f"{stem}.h", names, cpp_namespace)
            namespace = "Sts2.Generated." + ".".join(part.upper() for part in rel.parent.parts)
            write_csharp(ROOT / "generated" / "csharp" / rel.parent / f"{proto.stem}.cs", names, namespace)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
