from __future__ import annotations

from dataclasses import dataclass
import re


@dataclass(frozen=True)
class Field:
    number: int
    name: str
    type_name: str


@dataclass(frozen=True)
class ProtoContract:
    path: str
    major: int
    minor: int
    messages: dict[str, dict[int, Field]]


@dataclass(frozen=True)
class CompatibilityResult:
    ok: bool
    messages: list[str]


_VERSION_RE = re.compile(r"sts2\.schema\.(major|minor):\s*(\d+)")
_MESSAGE_RE = re.compile(r"message\s+(\w+)\s*\{(?P<body>.*?)\}", re.DOTALL)
_FIELD_RE = re.compile(r"^\s*(?:repeated\s+)?([\w.]+)\s+(\w+)\s*=\s*(\d+)\s*;", re.MULTILINE)


def parse_proto_contract(path: str, text: str) -> ProtoContract:
    versions = {name: int(value) for name, value in _VERSION_RE.findall(text)}
    if "major" not in versions or "minor" not in versions:
        raise ValueError(f"{path}: missing sts2.schema major/minor comments")

    messages: dict[str, dict[int, Field]] = {}
    for message in _MESSAGE_RE.finditer(text):
        fields: dict[int, Field] = {}
        for type_name, field_name, number_text in _FIELD_RE.findall(message.group("body")):
            number = int(number_text)
            fields[number] = Field(number=number, name=field_name, type_name=type_name)
        messages[message.group(1)] = fields

    return ProtoContract(
        path=path,
        major=versions["major"],
        minor=versions["minor"],
        messages=messages,
    )


def compare_contracts(baseline: ProtoContract, candidate: ProtoContract) -> CompatibilityResult:
    messages: list[str] = []
    if candidate.major != baseline.major:
        messages.append(f"major version changed: {baseline.major} -> {candidate.major}")
    if candidate.minor < baseline.minor:
        messages.append(f"minor version regressed: {baseline.minor} -> {candidate.minor}")

    for message_name, baseline_fields in baseline.messages.items():
        candidate_fields = candidate.messages.get(message_name)
        if candidate_fields is None:
            messages.append(f"removed message {message_name}")
            continue
        for number, baseline_field in baseline_fields.items():
            candidate_field = candidate_fields.get(number)
            if candidate_field is None:
                messages.append(f"removed field {message_name}.{baseline_field.name} = {number}")
            elif candidate_field.name != baseline_field.name or candidate_field.type_name != baseline_field.type_name:
                messages.append(f"changed field {message_name}.{number}")

    return CompatibilityResult(ok=not messages, messages=messages)
