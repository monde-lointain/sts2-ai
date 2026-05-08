"""AST analyzer for sts2-ai. Maps classes, methods, control flow, and def-use.

Run from repo root (after creating a venv with ``libclang`` installed):
    .venv\\Scripts\\python.exe tools\\ast-analyzer\\sts2_ast_analyzer.py    # Windows
    .venv/bin/python tools/ast-analyzer/sts2_ast_analyzer.py                # Linux/macOS

Outputs JSON to stdout (or --out PATH). The document generator then turns
that JSON into the analytical report in docs/test-plan/.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from collections import defaultdict
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Iterable

import clang.cindex as ci

REPO_ROOT = Path(__file__).resolve().parents[2]
SRC_DIR = REPO_ROOT / "src"
INCLUDE_DIR = REPO_ROOT / "include"


def _detect_resource_dir() -> Path | None:
    """Locate clang's builtin header dir (stddef.h, stdarg.h, ...).

    The PyPI ``libclang`` wheel ships only the shared library, not the
    accompanying ``clang/<ver>/include`` resource directory. Without it,
    parsing fails on system headers that pull in ``<stddef.h>``. Prefer
    asking a system ``clang`` for its resource-dir; otherwise probe a few
    common Linux locations.
    """
    clang_bin = shutil.which("clang") or shutil.which("clang++")
    if clang_bin:
        try:
            out = subprocess.check_output(
                [clang_bin, "-print-resource-dir"],
                stderr=subprocess.DEVNULL, text=True,
            ).strip()
            cand = Path(out) / "include"
            if (cand / "stddef.h").is_file():
                return cand
        except (subprocess.CalledProcessError, OSError):
            pass
    for base in ("/usr/lib/llvm-18", "/usr/lib/llvm-17", "/usr/lib/llvm-16",
                 "/usr/lib/llvm-15", "/usr/lib/llvm-14"):
        for p in Path(base).glob("lib/clang/*/include/stddef.h"):
            return p.parent
    return None


CXX_ARGS = [
    "-x", "c++",
    "-std=c++20",
    f"-I{INCLUDE_DIR}",
    f"-I{SRC_DIR}",
]
_resource_include = _detect_resource_dir()
if _resource_include is not None:
    # ``-isystem`` so the builtin headers come ahead of libstdc++ and
    # don't shadow user includes.
    CXX_ARGS.extend(["-isystem", str(_resource_include)])
else:
    print(
        "WARN: clang resource-dir not found. System headers may not resolve, "
        "leading to incomplete AST. Install clang on PATH or add a resource-dir probe "
        "for your platform in _detect_resource_dir().",
        file=sys.stderr,
    )

# Cursor kinds that contribute to cyclomatic complexity / branch coverage.
LOOP_KINDS = {
    ci.CursorKind.WHILE_STMT,
    ci.CursorKind.FOR_STMT,
    ci.CursorKind.CXX_FOR_RANGE_STMT,
    ci.CursorKind.DO_STMT,
}
DECISION_BRANCHES = {
    # kind                          (cyclomatic+, branches+)
    ci.CursorKind.IF_STMT:          (1, 2),
    ci.CursorKind.WHILE_STMT:       (1, 2),
    ci.CursorKind.FOR_STMT:         (1, 2),
    ci.CursorKind.CXX_FOR_RANGE_STMT:(1, 2),
    ci.CursorKind.DO_STMT:          (1, 2),
    ci.CursorKind.CONDITIONAL_OPERATOR: (1, 2),  # a ? b : c
    ci.CursorKind.CASE_STMT:        (1, 1),
    ci.CursorKind.DEFAULT_STMT:     (0, 1),
    ci.CursorKind.CXX_CATCH_STMT:   (1, 1),
}


def is_in_project(loc) -> bool:
    if loc.file is None:
        return False
    p = Path(loc.file.name)
    for root in (SRC_DIR, INCLUDE_DIR):
        try:
            p.relative_to(root)
            return True
        except ValueError:
            continue
    return False


def cursor_text(c: ci.Cursor) -> str:
    """Best-effort reconstruction of source text from tokens."""
    extent = c.extent
    if extent.start.file is None:
        return ""
    try:
        toks = list(c.get_tokens())
    except Exception:
        return ""
    return " ".join(t.spelling for t in toks)


def short_text(c: ci.Cursor, limit: int = 80) -> str:
    s = cursor_text(c)
    if len(s) > limit:
        s = s[: limit - 1] + "…"
    return s


def is_short_circuit_binop(c: ci.Cursor) -> str | None:
    """If c is `&&` or `||`, return the operator string; else None."""
    if c.kind != ci.CursorKind.BINARY_OPERATOR:
        return None
    children = list(c.get_children())
    if len(children) != 2:
        return None
    # Operator is the first token between LHS extent end and RHS extent start.
    lhs_end = children[0].extent.end
    rhs_start = children[1].extent.start
    for t in c.get_tokens():
        s = t.spelling
        if s in ("&&", "||"):
            tloc = t.extent.start
            if (tloc.line, tloc.column) >= (lhs_end.line, lhs_end.column) and \
               (tloc.line, tloc.column) <= (rhs_start.line, rhs_start.column):
                return s
    return None


def assignment_op(c: ci.Cursor) -> str | None:
    """If c is `=` / `+=` / `-=` / `*=` / `/=` / `%=` etc., return op text."""
    if c.kind != ci.CursorKind.BINARY_OPERATOR and \
       c.kind != ci.CursorKind.COMPOUND_ASSIGNMENT_OPERATOR:
        return None
    children = list(c.get_children())
    if len(children) != 2:
        return None
    lhs_end = children[0].extent.end
    rhs_start = children[1].extent.start
    for t in c.get_tokens():
        s = t.spelling
        if s in ("=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>="):
            tloc = t.extent.start
            if (tloc.line, tloc.column) >= (lhs_end.line, lhs_end.column) and \
               (tloc.line, tloc.column) <= (rhs_start.line, rhs_start.column):
                return s
    return None


def unary_inc_dec(c: ci.Cursor) -> str | None:
    if c.kind != ci.CursorKind.UNARY_OPERATOR:
        return None
    for t in c.get_tokens():
        if t.spelling in ("++", "--"):
            return t.spelling
    return None


@dataclass
class DefUse:
    name: str
    decl_kind: str           # parameter | local | member | unknown
    decl_line: int           # 0 if unknown (e.g. parameter on method)
    defs: list[dict] = field(default_factory=list)  # [{line, kind: init/assign/compound/increment}]
    uses: list[int] = field(default_factory=list)


@dataclass
class FuncInfo:
    qualified: str           # "Combat::play_card", "powers::find#1", etc.
    display_name: str
    spelling: str
    file: str
    line: int
    end_line: int
    parent_kind: str         # class | struct | namespace | global
    parent_name: str
    is_method: bool
    is_static: bool
    is_const: bool
    is_template: bool
    is_overload: int         # 0 = unique, otherwise 1-based overload index
    return_type: str
    parameters: list[dict]   # [{name, type}]
    decisions: list[dict] = field(default_factory=list)
    cyclomatic_complexity: int = 1
    branch_count: int = 0    # number of independent branches to cover
    statements: int = 0
    def_use: dict[str, dict] = field(default_factory=dict)


@dataclass
class TypeInfo:
    name: str
    kind: str                # class | struct
    file: str
    line: int
    fields: list[dict]       # [{name, type, default?}]
    methods: list[str]       # qualified names referencing FuncInfo
    has_virtual: bool
    is_polymorphic: bool


@dataclass
class NamespaceInfo:
    name: str
    files: list[str]
    free_functions: list[str]   # qualified names


# -----------------------------------------------------------------------------
# Pass 1: enumerate types and free functions

class WorldModel:
    def __init__(self):
        self.types: dict[str, TypeInfo] = {}
        self.funcs: dict[str, FuncInfo] = {}
        self.namespaces: dict[str, NamespaceInfo] = {}
        self._overloads: dict[str, int] = defaultdict(int)
        self._seen_funcs: set[tuple[str, str, int]] = set()

    def func_key(self, fn: FuncInfo) -> str:
        return f"{fn.file}:{fn.line}:{fn.spelling}"

    def add_type(self, c: ci.Cursor) -> TypeInfo:
        kind = "class" if c.kind == ci.CursorKind.CLASS_DECL else "struct"
        loc = c.location
        rel = str(Path(loc.file.name).relative_to(REPO_ROOT)).replace("\\", "/")
        # Build qualified name (e.g. "sts2::game::Combat") so methods whose
        # parent_name comes from parent_qualifier match this key.
        _, parent_qual = parent_qualifier(c)
        spelling = c.spelling or c.displayname
        name = f"{parent_qual}::{spelling}" if parent_qual else spelling
        ti = TypeInfo(
            name=name, kind=kind, file=rel, line=loc.line,
            fields=[], methods=[], has_virtual=False, is_polymorphic=False,
        )
        for ch in c.get_children():
            if ch.kind == ci.CursorKind.FIELD_DECL:
                ti.fields.append({"name": ch.spelling, "type": ch.type.spelling})
            elif ch.kind in (ci.CursorKind.CXX_METHOD,
                             ci.CursorKind.CONSTRUCTOR,
                             ci.CursorKind.DESTRUCTOR):
                if ch.is_virtual_method() or ch.is_pure_virtual_method():
                    ti.has_virtual = True
                    ti.is_polymorphic = True
        if name not in self.types:
            self.types[name] = ti
        else:
            # Merge fields/methods if header is included via multiple TUs.
            existing = self.types[name]
            for f in ti.fields:
                if f not in existing.fields:
                    existing.fields.append(f)
            existing.has_virtual = existing.has_virtual or ti.has_virtual
            existing.is_polymorphic = existing.is_polymorphic or ti.is_polymorphic
        return self.types[name]


def parent_qualifier(c: ci.Cursor) -> tuple[str, str]:
    """Return (parent_kind, parent_qualified_name) walking semantic_parent."""
    p = c.semantic_parent
    chain = []
    kind = "global"
    while p is not None and p.kind != ci.CursorKind.TRANSLATION_UNIT:
        chain.append(p.spelling or "")
        if p.kind in (ci.CursorKind.CLASS_DECL, ci.CursorKind.STRUCT_DECL):
            kind = "class" if p.kind == ci.CursorKind.CLASS_DECL else "struct"
        elif p.kind == ci.CursorKind.NAMESPACE:
            if kind == "global":
                kind = "namespace"
        p = p.semantic_parent
    chain.reverse()
    return kind, "::".join(s for s in chain if s)


def build_function(c: ci.Cursor, world: WorldModel) -> FuncInfo | None:
    if not c.is_definition():
        return None
    if c.kind not in (
        ci.CursorKind.FUNCTION_DECL,
        ci.CursorKind.CXX_METHOD,
        ci.CursorKind.CONSTRUCTOR,
        ci.CursorKind.DESTRUCTOR,
        ci.CursorKind.FUNCTION_TEMPLATE,
    ):
        return None
    if not is_in_project(c.location):
        return None

    rel = str(Path(c.location.file.name).relative_to(REPO_ROOT)).replace("\\", "/")
    parent_kind, parent_name = parent_qualifier(c)
    is_method = c.kind in (ci.CursorKind.CXX_METHOD,
                           ci.CursorKind.CONSTRUCTOR,
                           ci.CursorKind.DESTRUCTOR)
    # A FUNCTION_TEMPLATE whose parent is a class/struct is a member-function
    # template (e.g. Rng::shuffle). Treat it as a method for grouping.
    if c.kind == ci.CursorKind.FUNCTION_TEMPLATE and parent_kind in ("class", "struct"):
        is_method = True
    qual = f"{parent_name}::{c.spelling}" if parent_name else c.spelling

    # Overload index
    world._overloads[qual] += 1
    ovr = world._overloads[qual]

    # Dedup across TUs
    key = (rel, c.spelling, c.location.line)
    if key in world._seen_funcs:
        return None
    world._seen_funcs.add(key)

    params = [{"name": a.spelling, "type": a.type.spelling}
              for a in c.get_arguments()]
    fn = FuncInfo(
        qualified=qual + (f"#{ovr}" if ovr > 1 else ""),
        display_name=c.displayname,
        spelling=c.spelling,
        file=rel,
        line=c.location.line,
        end_line=c.extent.end.line,
        parent_kind=parent_kind,
        parent_name=parent_name,
        is_method=is_method,
        is_static=c.is_static_method() if is_method else False,
        is_const=c.is_const_method() if is_method else False,
        is_template=c.kind == ci.CursorKind.FUNCTION_TEMPLATE,
        is_overload=0 if ovr == 1 else ovr,
        return_type=c.result_type.spelling,
        parameters=params,
    )
    return fn


# -----------------------------------------------------------------------------
# Pass 2: walk function body for decisions and def-use

class BodyAnalyzer:
    def __init__(self, fn: FuncInfo):
        self.fn = fn
        # Track parameter declarations as initial defs.
        self.def_use: dict[str, DefUse] = {}

    def get_or_create(self, decl_cursor: ci.Cursor, fallback_name: str = "") -> DefUse:
        name = decl_cursor.spelling if decl_cursor and decl_cursor.spelling else fallback_name
        if not name:
            name = "<anon>"
        d = self.def_use.get(name)
        if d is None:
            kind = "unknown"
            line = 0
            if decl_cursor is not None:
                if decl_cursor.kind == ci.CursorKind.PARM_DECL:
                    kind = "parameter"
                elif decl_cursor.kind == ci.CursorKind.VAR_DECL:
                    kind = "local"
                elif decl_cursor.kind == ci.CursorKind.FIELD_DECL:
                    kind = "member"
                if decl_cursor.location.file:
                    line = decl_cursor.location.line
            d = DefUse(name=name, decl_kind=kind, decl_line=line)
            self.def_use[name] = d
        return d

    def add_def(self, decl_cursor: ci.Cursor, line: int, kind: str, name: str = ""):
        d = self.get_or_create(decl_cursor, name)
        d.defs.append({"line": line, "kind": kind})

    def add_use(self, decl_cursor: ci.Cursor, line: int, name: str = ""):
        d = self.get_or_create(decl_cursor, name)
        if not d.uses or d.uses[-1] != line:
            d.uses.append(line)

    def analyze(self, c: ci.Cursor):
        # Seed parameters as defs at their declaration line.
        for child in c.get_children():
            if child.kind == ci.CursorKind.PARM_DECL:
                d = self.get_or_create(child)
                d.defs.append({"line": child.location.line, "kind": "parameter"})

        body = next((ch for ch in c.get_children()
                     if ch.kind == ci.CursorKind.COMPOUND_STMT
                     or ch.kind == ci.CursorKind.CXX_TRY_STMT), None)
        if body is None:
            return
        self._walk(body)

    def _walk(self, cursor: ci.Cursor):
        # Statement counter: count immediate statement children of compound stmts.
        # Decisions: collected here.
        # Def-use: collected here.
        for c in cursor.walk_preorder():
            if not is_in_project(c.location):
                continue
            kind = c.kind

            # ---- Statement counting (rough)
            if kind in (ci.CursorKind.DECL_STMT,
                        ci.CursorKind.RETURN_STMT,
                        ci.CursorKind.IF_STMT, ci.CursorKind.SWITCH_STMT,
                        ci.CursorKind.WHILE_STMT, ci.CursorKind.FOR_STMT,
                        ci.CursorKind.CXX_FOR_RANGE_STMT, ci.CursorKind.DO_STMT,
                        ci.CursorKind.BREAK_STMT, ci.CursorKind.CONTINUE_STMT,
                        ci.CursorKind.CXX_TRY_STMT, ci.CursorKind.CXX_THROW_EXPR):
                self.fn.statements += 1

            # ---- Decisions
            if kind in DECISION_BRANCHES:
                cc, br = DECISION_BRANCHES[kind]
                cond_text = ""
                # for IF_STMT etc., first child is the condition
                children = list(c.get_children())
                if kind == ci.CursorKind.IF_STMT and children:
                    cond_text = short_text(children[0])
                elif kind in LOOP_KINDS and children:
                    # for-stmt has init/cond/inc/body; while has cond/body
                    if kind == ci.CursorKind.FOR_STMT:
                        # children may be: [init], [cond], [inc], body
                        # Use the cond (often the second child) if recognizable
                        cond_text = short_text(c, 90)
                    else:
                        cond_text = short_text(children[0])
                elif kind == ci.CursorKind.CONDITIONAL_OPERATOR and children:
                    cond_text = short_text(children[0])
                elif kind == ci.CursorKind.CASE_STMT and children:
                    cond_text = "case " + short_text(children[0])
                elif kind == ci.CursorKind.DEFAULT_STMT:
                    cond_text = "default"
                self.fn.decisions.append({
                    "kind": kind.name.lower().replace("_stmt", "").replace("_operator", ""),
                    "line": c.location.line,
                    "expr": cond_text,
                })
                self.fn.cyclomatic_complexity += cc
                self.fn.branch_count += br

            # short-circuit && / ||
            sc = is_short_circuit_binop(c)
            if sc:
                self.fn.decisions.append({
                    "kind": "short_circuit",
                    "line": c.location.line,
                    "expr": sc,
                })
                self.fn.cyclomatic_complexity += 1
                self.fn.branch_count += 2

            # ---- Def-use
            if kind == ci.CursorKind.VAR_DECL:
                # local def. Has init?
                inits = [ch for ch in c.get_children()
                         if ch.kind not in (ci.CursorKind.TYPE_REF,
                                            ci.CursorKind.NAMESPACE_REF,
                                            ci.CursorKind.TEMPLATE_REF)]
                kind_label = "init" if inits else "decl"
                self.add_def(c, c.location.line, kind_label)
            elif kind == ci.CursorKind.BINARY_OPERATOR:
                op = assignment_op(c)
                if op == "=":
                    children = list(c.get_children())
                    lhs = children[0] if children else None
                    target = lhs.referenced if lhs is not None else None
                    if target is not None:
                        self.add_def(target, c.location.line, "assign",
                                     name=lhs.spelling if lhs else "")
            elif kind == ci.CursorKind.COMPOUND_ASSIGNMENT_OPERATOR:
                children = list(c.get_children())
                lhs = children[0] if children else None
                target = lhs.referenced if lhs is not None else None
                if target is not None:
                    # compound implies use+def
                    self.add_use(target, c.location.line,
                                 name=lhs.spelling if lhs else "")
                    self.add_def(target, c.location.line, "compound",
                                 name=lhs.spelling if lhs else "")
            elif kind == ci.CursorKind.UNARY_OPERATOR:
                op = unary_inc_dec(c)
                if op:
                    children = list(c.get_children())
                    operand = children[0] if children else None
                    target = operand.referenced if operand is not None else None
                    if target is not None:
                        self.add_use(target, c.location.line,
                                     name=operand.spelling if operand else "")
                        self.add_def(target, c.location.line, "increment",
                                     name=operand.spelling if operand else "")
            elif kind == ci.CursorKind.DECL_REF_EXPR:
                target = c.referenced
                if target is not None and target.kind in (
                    ci.CursorKind.VAR_DECL,
                    ci.CursorKind.PARM_DECL,
                    ci.CursorKind.FIELD_DECL,
                ):
                    # Conservative: any read of a var/parm we register as use.
                    self.add_use(target, c.location.line, name=c.spelling)
            elif kind == ci.CursorKind.MEMBER_REF_EXPR:
                target = c.referenced
                if target is not None and target.kind == ci.CursorKind.FIELD_DECL:
                    self.add_use(target, c.location.line, name=c.spelling)


# -----------------------------------------------------------------------------
# Driver

TU_SOURCES = [
    "src/game/rng.cc",
    "src/game/powers.cc",
    "src/game/damage.cc",
    "src/game/cards.cc",
    "src/game/enemies.cc",
    "src/game/combat.cc",
    "src/render/bar.cc",
    "src/render/console.cc",
    "src/render/render.cc",
    "src/input/input.cc",
    "src/main.cc",
]


def parse_tu(index: ci.Index, path: Path) -> ci.TranslationUnit:
    tu = index.parse(str(path), args=CXX_ARGS,
                     options=ci.TranslationUnit.PARSE_DETAILED_PROCESSING_RECORD)
    return tu


def collect_decls(c: ci.Cursor, world: WorldModel):
    if c.kind in (ci.CursorKind.CLASS_DECL, ci.CursorKind.STRUCT_DECL):
        if c.is_definition() and is_in_project(c.location):
            world.add_type(c)
    fn = build_function(c, world)
    if fn is not None:
        ba = BodyAnalyzer(fn)
        ba.analyze(c)
        # Compact def-use into dict of dicts
        for name, du in ba.def_use.items():
            fn.def_use[name] = asdict(du)
        world.funcs[fn.qualified] = fn
        # Attach to type if method
        if fn.is_method and fn.parent_name in world.types:
            world.types[fn.parent_name].methods.append(fn.qualified)
        # Attach to namespace if free fn under one
        if not fn.is_method and fn.parent_kind == "namespace" and fn.parent_name:
            ns = world.namespaces.setdefault(fn.parent_name,
                                              NamespaceInfo(name=fn.parent_name,
                                                            files=[], free_functions=[]))
            if fn.file not in ns.files:
                ns.files.append(fn.file)
            ns.free_functions.append(fn.qualified)
    for ch in c.get_children():
        collect_decls(ch, world)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=None,
                    help="Path to write JSON output (default: stdout)")
    args = ap.parse_args(argv)

    index = ci.Index.create()
    world = WorldModel()

    diagnostics: list[str] = []
    for src in TU_SOURCES:
        tu = parse_tu(index, REPO_ROOT / src)
        for d in tu.diagnostics:
            if d.severity >= ci.Diagnostic.Error:
                diagnostics.append(f"[{src}] {d.spelling} @ {d.location}")
        collect_decls(tu.cursor, world)

    if diagnostics:
        sys.stderr.write("\n".join(diagnostics) + "\n")

    payload = {
        "types": {k: asdict(v) for k, v in sorted(world.types.items())},
        "namespaces": {k: asdict(v) for k, v in sorted(world.namespaces.items())},
        "functions": {k: asdict(v) for k, v in sorted(world.funcs.items())},
    }

    text = json.dumps(payload, indent=2, sort_keys=False)
    if args.out:
        args.out.write_text(text, encoding="utf-8")
        print(f"Wrote {args.out} ({len(text)} bytes, "
              f"{len(world.types)} types, {len(world.funcs)} funcs).",
              file=sys.stderr)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
