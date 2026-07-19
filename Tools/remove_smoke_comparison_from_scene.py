import re
from pathlib import Path

scene_path = Path(__file__).resolve().parents[1] / "Assets/Scenes/SampleScene.unity"
text = scene_path.read_text(encoding="utf-8")
header, *docs = re.split(r"(--- !u!)", text)
if not header.startswith("%YAML"):
    raise SystemExit("Unexpected scene format")

blocks = {}
order = []
for i in range(0, len(docs), 2):
    if i + 1 >= len(docs):
        break
    prefix = docs[i]
    body = docs[i + 1]
    m = re.match(r"(\d+) &([^\n]+)\n", body)
    if not m:
        continue
    obj_id = m.group(2).strip()
    blocks[obj_id] = prefix + body
    order.append(obj_id)


def child_ids(content: str) -> list[str]:
    idx = content.find("m_Children:")
    if idx < 0:
        return []
    end = content.find("m_Father:", idx)
    if end < 0:
        end = idx + 1200
    section = content[idx:end]
    return re.findall(r"\{fileID: (\d+)\}", section)


def linked_ids(content: str) -> list[str]:
    ids = []
    if "GameObject:" in content:
        ids.extend(re.findall(r"component: \{fileID: (\d+)\}", content))
    m = re.search(r"m_GameObject: \{fileID: (\d+)\}", content)
    if m:
        ids.append(m.group(1))
    m = re.search(r"m_PrefabInstance: \{fileID: (\d+)\}", content)
    if m and m.group(1) != "0":
        ids.append(m.group(1))
    ids.extend(child_ids(content))
    return ids


def collect(start_ids: list[str]) -> set[str]:
    remove = set()
    stack = list(start_ids)
    while stack:
        oid = stack.pop()
        if oid in remove or oid not in blocks:
            continue
        remove.add(oid)
        stack.extend(linked_ids(blocks[oid]))
    return remove


remove = set()
for obj_id, content in blocks.items():
    if "m_Name: SmokeVfxComparison" in content or re.search(r"m_Name: FX_Grenade_Smoke_", content):
        remove |= collect([obj_id])

# Prefab instances mentioning smoke prefabs
for obj_id, content in blocks.items():
    if "PrefabInstance:" in content and "FX_Grenade_Smoke_" in content:
        remove |= collect([obj_id])

# Stripped helper objects referencing removed prefab instances
changed = True
while changed:
    changed = False
    for obj_id, content in blocks.items():
        if obj_id in remove:
            continue
        if "stripped" in content and "FX_Grenade_Smoke_" in content:
            remove.add(obj_id)
            changed = True

out = header
removed = 0
for obj_id in order:
    content = blocks[obj_id]
    if obj_id in remove:
        removed += 1
        continue
    if "SceneRoots:" in content:
        content = re.sub(r"\n  - \{fileID: 1629448165\}\n", "\n", content)
        content = re.sub(r"\n  - \{fileID: 844826136\}\n", "\n", content)
    out += content

scene_path.write_text(out, encoding="utf-8")
print(f"Removed {removed} scene objects related to SmokeVfxComparison")
