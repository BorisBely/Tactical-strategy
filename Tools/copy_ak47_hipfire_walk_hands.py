#!/usr/bin/env python3
"""Copy AK47_0 right-hand HipFireWalk / HipFireCrouchWalk IK slots onto other guns."""
from __future__ import annotations

import random
import re
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
SOURCE = ROOT / "Assets/Prefabs/Weapons/AK/Equipped/Equipped_AK47_0.prefab"
DIRS = [
    ROOT / "Assets/Prefabs/Weapons/AK/Equipped",
    ROOT / "Assets/Prefabs/Weapons/M4/Equipped",
    ROOT / "Assets/Prefabs/Weapons/Standalone/Equipped",
]

GO_RE = re.compile(r"--- !u!1 &(\d+)\nGameObject:\n(.*?)(?=\n--- |\Z)", re.S)
TR_RE = re.compile(r"--- !u!4 &(\d+)\nTransform:\n(.*?)(?=\n--- |\Z)", re.S)
CHILD_RE = re.compile(r"- \{fileID: (\d+)\}")
VEC4_RE = re.compile(r"\{x: ([^,}]+), y: ([^,}]+), z: ([^,}]+), w: ([^}]+)\}")
VEC3_RE = re.compile(r"\{x: ([^,}]+), y: ([^,}]+), z: ([^}]+)\}")


def collect_ids(text: str) -> set[int]:
    return {int(m.group(1)) for m in re.finditer(r"&(\d+)", text)}


def new_id(used: set[int]) -> int:
    while True:
        i = random.randint(10**15, 9 * 10**17)
        if i not in used:
            used.add(i)
            return i


def parse_prefab(text: str):
    gos: dict[str, dict] = {}
    for m in GO_RE.finditer(text):
        body = m.group(2)
        name_m = re.search(r"m_Name: (.+)", body)
        comp_m = re.search(r"- component: \{fileID: (\d+)\}", body)
        gos[m.group(1)] = {
            "name": name_m.group(1).strip() if name_m else "",
            "tr": comp_m.group(1) if comp_m else "",
        }

    trs: dict[str, dict] = {}
    for m in TR_RE.finditer(text):
        body = m.group(2)
        go_m = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
        father_m = re.search(r"m_Father: \{fileID: (\d+)\}", body)
        rot_m = re.search(r"m_LocalRotation: (\{[^}]+\})", body)
        pos_m = re.search(r"m_LocalPosition: (\{[^}]+\})", body)
        euler_m = re.search(r"m_LocalEulerAnglesHint: (\{[^}]+\})", body)
        children_m = re.search(r"m_Children: \[\]|m_Children:\n((?:  - \{fileID: \d+\}\n)*)", body)
        children: list[str] = []
        if children_m and children_m.group(0) != "m_Children: []":
            children = CHILD_RE.findall(children_m.group(0))
        trs[m.group(1)] = {
            "go": go_m.group(1) if go_m else "",
            "father": father_m.group(1) if father_m else "0",
            "children": children,
            "rot": rot_m.group(1) if rot_m else "{x: 0, y: 0, z: 0, w: 1}",
            "pos": pos_m.group(1) if pos_m else "{x: 0, y: 0, z: 0}",
            "euler": euler_m.group(1) if euler_m else "{x: 0, y: 0, z: 0}",
        }

    tr_to_name = {}
    for go_id, go in gos.items():
        if go["tr"]:
            tr_to_name[go["tr"]] = go["name"]
    return gos, trs, tr_to_name


def find_named(gos, trs, tr_to_name, name: str) -> str | None:
    for tr_id, n in tr_to_name.items():
        if n == name:
            return tr_id
    return None


def child_named(trs, tr_to_name, parent_tr: str, name: str) -> str | None:
    parent = trs.get(parent_tr)
    if parent is None:
        return None
    for cid in parent["children"]:
        if tr_to_name.get(cid) == name:
            return cid
    return None


def empty_go(name: str, go_id: int, tr_id: int, father_id: str, pos: str, rot: str, euler: str) -> str:
    return (
        f"--- !u!1 &{go_id}\n"
        "GameObject:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  serializedVersion: 6\n"
        "  m_Component:\n"
        f"  - component: {{fileID: {tr_id}}}\n"
        "  m_Layer: 0\n"
        f"  m_Name: {name}\n"
        "  m_TagString: Untagged\n"
        "  m_Icon: {fileID: 0}\n"
        "  m_NavMeshLayer: 0\n"
        "  m_StaticEditorFlags: 0\n"
        "  m_IsActive: 1\n"
        f"--- !u!4 &{tr_id}\n"
        "Transform:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go_id}}}\n"
        "  serializedVersion: 2\n"
        f"  m_LocalRotation: {rot}\n"
        f"  m_LocalPosition: {pos}\n"
        "  m_LocalScale: {x: 1, y: 1, z: 1}\n"
        "  m_ConstrainProportionsScale: 0\n"
        "  m_Children: []\n"
        f"  m_Father: {{fileID: {father_id}}}\n"
        f"  m_LocalEulerAnglesHint: {euler}\n"
    )


def add_child_ref(text: str, parent_tr: str, child_tr: str) -> str:
    pattern = re.compile(rf"(--- !u!4 &{parent_tr}\nTransform:\n)(.*?)(\n--- |\Z)", re.S)

    def repl(m: re.Match) -> str:
        body = m.group(2)
        if f"fileID: {child_tr}" in body:
            return m.group(0)
        if "m_Children: []" in body:
            body = body.replace(
                "m_Children: []",
                f"m_Children:\n  - {{fileID: {child_tr}}}",
                1,
            )
        else:
            body = re.sub(
                r"(m_Children:\n(?:  - \{fileID: \d+\}\n)*)",
                rf"\g<1>  - {{fileID: {child_tr}}}\n",
                body,
                count=1,
            )
        return m.group(1) + body + m.group(3)

    return pattern.sub(repl, text, count=1)


def set_trs(text: str, tr_id: str, pos: str, rot: str, euler: str) -> str:
    pattern = re.compile(rf"(--- !u!4 &{tr_id}\nTransform:\n)(.*?)(\n--- |\Z)", re.S)

    def repl(m: re.Match) -> str:
        body = m.group(2)
        body = re.sub(r"m_LocalRotation: \{[^}]+\}", f"m_LocalRotation: {rot}", body, count=1)
        body = re.sub(r"m_LocalPosition: \{[^}]+\}", f"m_LocalPosition: {pos}", body, count=1)
        body = re.sub(
            r"m_LocalEulerAnglesHint: \{[^}]+\}",
            f"m_LocalEulerAnglesHint: {euler}",
            body,
            count=1,
        )
        return m.group(1) + body + m.group(3)

    return pattern.sub(repl, text, count=1)


def wire_grip_fields(text: str, refs: dict[str, str]) -> str:
    if "m_StandingHipFireWalk:" in text:
        for field, fid in refs.items():
            text = re.sub(
                rf"{field}: \{{fileID: \d+\}}",
                f"{field}: {{fileID: {fid}}}",
                text,
                count=1,
            )
        return text

    text = re.sub(
        r"(m_StandingHipFire: \{fileID: \d+\}\n)",
        rf"\1  m_StandingHipFireWalk: {{fileID: {refs['m_StandingHipFireWalk']}}}\n"
        rf"  m_StandingHipFireCrouchWalk: {{fileID: {refs['m_StandingHipFireCrouchWalk']}}}\n",
        text,
        count=1,
    )
    text = re.sub(
        r"(m_CrouchHipFire: \{fileID: \d+\}\n)",
        rf"\1  m_CrouchHipFireWalk: {{fileID: {refs['m_CrouchHipFireWalk']}}}\n"
        rf"  m_CrouchHipFireCrouchWalk: {{fileID: {refs['m_CrouchHipFireCrouchWalk']}}}\n",
        text,
        count=1,
    )
    text = re.sub(
        r"(m_VehicleHipFire: \{fileID: \d+\}\n)",
        rf"\1  m_VehicleHipFireWalk: {{fileID: {refs['m_VehicleHipFireWalk']}}}\n"
        rf"  m_VehicleHipFireCrouchWalk: {{fileID: {refs['m_VehicleHipFireCrouchWalk']}}}\n",
        text,
        count=1,
    )
    return text


def ensure_slot(text: str, used: set[int], parent_tr: str, name: str, pos: str, rot: str, euler: str) -> tuple[str, str]:
    gos, trs, tr_to_name = parse_prefab(text)
    existing = child_named(trs, tr_to_name, parent_tr, name)
    if existing:
        text = set_trs(text, existing, pos, rot, euler)
        return text, existing

    go_id = new_id(used)
    tr_id = new_id(used)
    chunk = empty_go(name, go_id, tr_id, parent_tr, pos, rot, euler)
    if not text.endswith("\n"):
        text += "\n"
    text += chunk
    text = add_child_ref(text, parent_tr, str(tr_id))
    return text, str(tr_id)


def process_prefab(path: Path, walk_pos: str, walk_rot: str, walk_eu: str, crouch_pos: str, crouch_rot: str, crouch_eu: str) -> str:
    raw = path.read_bytes()
    nl = b"\r\n" if b"\r\n" in raw else b"\n"
    text = raw.decode("utf-8")
    used = collect_ids(text)
    gos, trs, tr_to_name = parse_prefab(text)

    right = find_named(gos, trs, tr_to_name, "RightHandIK") or find_named(gos, trs, tr_to_name, "RightHand")
    if right is None:
        return f"SKIP no RightHandIK: {path.name}"

    standing = child_named(trs, tr_to_name, right, "Standing")
    crouch = child_named(trs, tr_to_name, right, "Crouch")
    vehicle = child_named(trs, tr_to_name, right, "Vehicle")
    if standing is None or crouch is None:
        return f"SKIP missing stance: {path.name}"

    def hip_trs(stance_tr: str) -> tuple[str, str, str]:
        hip = child_named(trs, tr_to_name, stance_tr, "HipFire")
        if hip is None:
            return "{x: 0, y: 0, z: 0}", "{x: 0, y: 0, z: 0, w: 1}", "{x: 0, y: 0, z: 0}"
        slot = trs[hip]
        return slot["pos"], slot["rot"], slot["euler"]

    refs: dict[str, str] = {}

    # Authored copies from AK47_0.
    text, refs["m_StandingHipFireWalk"] = ensure_slot(
        text, used, standing, "HipFireWalk", walk_pos, walk_rot, walk_eu
    )
    text, refs["m_CrouchHipFireCrouchWalk"] = ensure_slot(
        text, used, crouch, "HipFireCrouchWalk", crouch_pos, crouch_rot, crouch_eu
    )

    # Remaining slots of these two poses: seed from this gun's own HipFire.
    gos, trs, tr_to_name = parse_prefab(text)
    for stance_tr, walk_field, crouch_walk_field in (
        (standing, None, "m_StandingHipFireCrouchWalk"),
        (crouch, "m_CrouchHipFireWalk", None),
        (vehicle, "m_VehicleHipFireWalk", "m_VehicleHipFireCrouchWalk") if vehicle else (None, None, None),
    ):
        if stance_tr is None:
            continue
        pos, rot, eu = hip_trs(stance_tr)
        if walk_field:
            existing = child_named(trs, tr_to_name, stance_tr, "HipFireWalk")
            if existing:
                refs[walk_field] = existing
            else:
                text, refs[walk_field] = ensure_slot(text, used, stance_tr, "HipFireWalk", pos, rot, eu)
                gos, trs, tr_to_name = parse_prefab(text)
        if crouch_walk_field:
            existing = child_named(trs, tr_to_name, stance_tr, "HipFireCrouchWalk")
            if existing:
                refs[crouch_walk_field] = existing
            else:
                text, refs[crouch_walk_field] = ensure_slot(
                    text, used, stance_tr, "HipFireCrouchWalk", pos, rot, eu
                )
                gos, trs, tr_to_name = parse_prefab(text)

    if vehicle is None:
        refs.setdefault("m_VehicleHipFireWalk", "0")
        refs.setdefault("m_VehicleHipFireCrouchWalk", "0")

    text = wire_grip_fields(text, refs)
    out = text.encode("utf-8")
    if nl == b"\r\n":
        out = out.replace(b"\n", b"\r\n").replace(b"\r\r\n", b"\r\n")
    path.write_bytes(out)
    return f"OK {path.name}"


def main() -> None:
	import argparse

	parser = argparse.ArgumentParser()
	parser.add_argument("--source", default=str(SOURCE))
	parser.add_argument(
		"--dir",
		action="append",
		dest="dirs",
		help="Equipped prefab folder. Repeatable. Default: AK + M4 + Standalone.",
	)
	args = parser.parse_args()
	source = Path(args.source)
	if not source.is_absolute():
		source = ROOT / source
	folders = [Path(d) if Path(d).is_absolute() else ROOT / d for d in (args.dirs or DIRS)]

	src = source.read_text(encoding="utf-8")
	gos, trs, tr_to_name = parse_prefab(src)
	right = find_named(gos, trs, tr_to_name, "RightHandIK")
	standing = child_named(trs, tr_to_name, right, "Standing")
	crouch = child_named(trs, tr_to_name, right, "Crouch")
	walk = child_named(trs, tr_to_name, standing, "HipFireWalk")
	crouch_walk = child_named(trs, tr_to_name, crouch, "HipFireCrouchWalk")
	if walk is None or crouch_walk is None:
		raise SystemExit(f"{source.name} missing Standing/HipFireWalk or Crouch/HipFireCrouchWalk")

	walk_slot = trs[walk]
	crouch_slot = trs[crouch_walk]
	print("SOURCE", source.name)
	print("  Standing/HipFireWalk", walk_slot["pos"], walk_slot["rot"])
	print("  Crouch/HipFireCrouchWalk", crouch_slot["pos"], crouch_slot["rot"])

	reports = []
	for folder in folders:
		for prefab in sorted(folder.glob("Equipped_*.prefab")):
			if prefab.resolve() == source.resolve():
				reports.append(f"SKIP source {prefab.name}")
				continue
			reports.append(
				process_prefab(
					prefab,
					walk_slot["pos"],
					walk_slot["rot"],
					walk_slot["euler"],
					crouch_slot["pos"],
					crouch_slot["rot"],
					crouch_slot["euler"],
				)
			)
	print("\n".join(reports))


if __name__ == "__main__":
    main()
