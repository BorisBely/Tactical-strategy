import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENE = os.path.join(ROOT, "Assets/Scenes/SampleScene.unity")

TARGET_FIXES = [
    ("1032073188", "Sphere100", 3, 100),
    ("1717102754", "Sphere50", 0, 50),
    ("1992022200", "Sphere150", 6, 150),
    ("1992022210", "Sphere200", 9, 200),
    ("1992022220", "Sphere250", 12, 250),
    ("1992022230", "Sphere300", 15, 300),
    ("1992022240", "Sphere350", 18, 350),
    ("1992022250", "Sphere400", 21, 400),
    ("1992022260", "Sphere450", 24, 450),
    ("1992022270", "Sphere500", 27, 500),
]


def fix_scene_targets() -> None:
    with open(SCENE, "r", encoding="utf-8") as file:
        text = file.read()

    for game_object_id, name, x, z in TARGET_FIXES:
        game_object_pattern = (
            rf"(--- !u!1 &{game_object_id}\nGameObject:[\s\S]*?  m_Name: )Sphere[^\n]+"
        )
        text, count = re.subn(game_object_pattern, rf"\g<1>{name}", text, count=1)
        if count != 1:
            raise RuntimeError(f"Failed to rename target {game_object_id} to {name}")

        transform_pattern = (
            rf"(--- !u!4 &[0-9]+\nTransform:[\s\S]*?  m_GameObject: \{{fileID: {game_object_id}\}}\n(?:.*\n){{0,6}}?  m_LocalPosition: \{{x: )[0-9.+-]+(, y: 1, z: )[0-9.+-]+(\}})"
        )
        text, count = re.subn(transform_pattern, rf"\g<1>{x}\g<2>{z}\g<3>", text, count=1)
        if count != 1:
            raise RuntimeError(f"Failed to reposition target {game_object_id}")

    with open(SCENE, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)
    print("Fixed shooting range targets in SampleScene.unity")


if __name__ == "__main__":
    fix_scene_targets()
