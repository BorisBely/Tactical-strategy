import glob
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

WEAPON_RANGES = {
    "Weapon_AK74U.asset": 275,
    "Weapon_MK18.asset": 300,
    "Weapon_AK74UMOD1.asset": 325,
    "Weapon_AK47S.asset": 375,
    "Weapon_M4_ModA_1.asset": 475,
    "Weapon_AK47.asset": 475,
    "Weapon_AK47_1.asset": 525,
    "Weapon_AK47MOD1.asset": 550,
    "Weapon_AK74.asset": 525,
    "Weapon_M4_ModA_2.asset": 525,
    "Weapon_AK74MOD1.asset": 575,
    "Weapon_RPK47.asset": 625,
    "Weapon_RPK47MOD1.asset": 675,
    "Weapon_M16A_ModA_1.asset": 625,
    "Weapon_RPK74.asset": 675,
    "Weapon_RPK74MOD1.asset": 725,
    "Weapon_M16A4_ModA_2.asset": 700,
    "Weapon_MK12.asset": 800,
}

LABEL_REPLACEMENTS = [
    ("0–15 м, пересечение 25 м", "0–75 м, пересечение 125 м"),
    ("0–15 м, пересечение 30 м", "0–75 м, пересечение 150 м"),
    ("10–20 м, пересечение 30 м", "50–100 м, пересечение 150 м"),
    ("20–45 м, пересечение 45 м", "100–225 м, пересечение 225 м"),
    ("0–20 м, пересечение 35 м", "0–100 м, пересечение 175 м"),
    ("0–20 и 35–55 м, пересечение 55 м", "0–100 и 175–275 м, пересечение 275 м"),
    ("0–60 м, пересечение 65 м", "0–300 м, пересечение 325 м"),
    ("0–50 м, пересечение 60 м", "0–250 м, пересечение 300 м"),
    ("35–55 м, пересечение 55 м", "175–275 м, пересечение 275 м"),
    ("40–50 м, пересечение 60 м", "200–250 м, пересечение 300 м"),
    ("0–15 и 40–50 м, пересечение 60 м", "0–75 и 200–250 м, пересечение 300 м"),
    ("30–50 м, пересечение 60 м", "150–250 м, пересечение 300 м"),
    ("60–70 м", "300–350 м"),
    ("70–80 м", "350–400 м"),
    ("80–100 м", "400–500 м"),
    ("40–50 м, пересечение 60 м", "200–250 м, пересечение 300 м"),
]


def stretch_k(match: re.Match[str]) -> str:
    dist = float(match.group(1))
    rest = match.group(2)
    new_dist = dist * 5
    if new_dist == int(new_dist):
        return f"K({int(new_dist)}f, {rest}"
    return f"K({new_dist}f, {rest}"


def stretch_optic_library() -> None:
    path = os.path.join(ROOT, r"Assets\_Scripts\Shooting\OpticDistanceCurveLibrary.cs")
    with open(path, "r", encoding="utf-8") as file:
        content = file.read()

    content = re.sub(r"K\((\d+(?:\.\d+)?)f,\s*([^)]+)\)", stretch_k, content)
    for old, new in LABEL_REPLACEMENTS:
        content = content.replace(old, new)

    with open(path, "w", encoding="utf-8", newline="\n") as file:
        file.write(content)
    print("Updated OpticDistanceCurveLibrary.cs")


def stretch_yaml_file(path: str, extra=None) -> bool:
    with open(path, "r", encoding="utf-8") as file:
        text = file.read()

    changed = False
    for curve_name in ("m_DispersionMultiplierByDistance", "m_AimTimeMultiplierByDistance"):
        pattern = curve_name + r":[\s\S]*?(?=\n\s{2,4}m_[A-Za-z]|\n\s{0,2}[A-Za-z#]|\Z)"

        def stretch_block(match: re.Match[str]) -> str:
            block = match.group(0)

            def repl_time(time_match: re.Match[str]) -> str:
                value = float(time_match.group(1))
                return f"time: {value * 5}"

            return re.sub(r"time:\s+([0-9.+-]+)", repl_time, block)

        new_text, count = re.subn(pattern, stretch_block, text)
        if count:
            text = new_text
            changed = True

    if extra:
        text, extra_count = extra(text)
        if extra_count:
            changed = True

    if changed:
        with open(path, "w", encoding="utf-8", newline="\n") as file:
            file.write(text)

    return changed


def stretch_weapon_assets() -> None:
    weapon_files = glob.glob(os.path.join(ROOT, "Assets/GameData/Shooting/**/Weapon_*.asset"), recursive=True)
    for path in weapon_files:
        name = os.path.basename(path)

        def weapon_extra(text: str, asset_name=name) -> tuple[str, int]:
            count = 0
            if asset_name in WEAPON_RANGES:
                text, replaced = re.subn(
                    r"m_EffectiveRangeMeters:\s+[0-9.]+",
                    f"m_EffectiveRangeMeters: {WEAPON_RANGES[asset_name]}",
                    text,
                    count=1,
                )
                count += replaced
            text, replaced = re.subn(
                r"m_MaxAudibleDistanceMeters:\s+125\b",
                "m_MaxAudibleDistanceMeters: 625",
                text,
            )
            count += replaced
            return text, count

        if stretch_yaml_file(path, weapon_extra):
            print("weapon", name)


def stretch_attachment_assets() -> None:
    attachment_files = glob.glob(os.path.join(ROOT, "Assets/GameData/Shooting/**/Attachment_*.asset"), recursive=True)
    for path in attachment_files:
        if stretch_yaml_file(path):
            print("attachment", os.path.basename(path))


def stretch_ammo_assets() -> None:
    for ammo_name in ("Ammo_556x45mmNATO.asset", "Ammo_545x39mm.asset", "Ammo_762x39mm.asset"):
        path = os.path.join(ROOT, "Assets/GameData/Shooting", ammo_name)
        with open(path, "r", encoding="utf-8") as file:
            text = file.read()
        text = re.sub(r"m_EffectiveRangeMeters:\s+100\b", "m_EffectiveRangeMeters: 500", text)
        with open(path, "w", encoding="utf-8", newline="\n") as file:
            file.write(text)
        print("ammo", ammo_name)


def stretch_scene_targets() -> None:
    scene_path = os.path.join(ROOT, "Assets/Scenes/SampleScene.unity")
    with open(scene_path, "r", encoding="utf-8") as file:
        text = file.read()

    for old_distance in range(10, 101, 10):
        new_distance = old_distance * 5
        text = text.replace(f"m_Name: Sphere{old_distance}", f"m_Name: Sphere{new_distance}")
        text = re.sub(
            rf"(m_GameObject: \{{fileID: \d+\}}\n(?:.*\n){{0,20}}?  m_LocalPosition: \{{x: 0, y: 1, z: ){old_distance}(\}})",
            rf"\g<1>{new_distance}\2",
            text,
            count=1,
        )

    text = text.replace(
        'm_TargetNamePattern: ^Sphere(10|20|30|40|50|60|70|80|90|100)$',
        'm_TargetNamePattern: ^Sphere(50|100|150|200|250|300|350|400|450|500)$',
    )
    text = text.replace("m_PlayerVisionRange: 120", "m_PlayerVisionRange: 550")
    text = text.replace("m_VisionRange: 18", "m_VisionRange: 500")
    text = text.replace("m_MaxDistanceMeters: 100", "m_MaxDistanceMeters: 500")

    with open(scene_path, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)
    print("Updated SampleScene.unity")


def main() -> None:
    raise SystemExit(
        "stretch_distance_balance.py is retired. Stage 9 range: bake_weapon_range.py. "
        "Stage 10 curves: bake_accuracy_aim_curves.py"
    )


if __name__ == "__main__":
    main()
