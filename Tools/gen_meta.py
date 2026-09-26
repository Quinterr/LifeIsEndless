#!/usr/bin/env python3
"""One-shot repo bootstrap helper.

1. Substitutes fixed GUID placeholders (@@GUID_*@@) in the hand-authored scene /
   asset / settings YAML files.
2. Generates Unity .meta files for every asset/folder under Assets/ that lacks one,
   using deterministic GUIDs for cross-referenced assets and random ones elsewhere.

Run from the repo root. Idempotent: existing .meta files are never overwritten.
"""

import os
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

# Fixed GUIDs for assets referenced across files (32-char lowercase hex).
FIXED = {
    "@@GUID_BOOTSTRAP@@": "7a1b0000000000000000000000000f01",  # WorldBootstrap.cs
    "@@GUID_INPUT@@":     "7a1b0000000000000000000000000f02",  # TimeControlInput.cs
    "@@GUID_HUD@@":       "7a1b0000000000000000000000000f03",  # SimHud.cs
    "@@GUID_WS_CS@@":     "7a1b0000000000000000000000000f04",  # WorldSettings.cs
    "@@GUID_GB_CS@@":     "7a1b0000000000000000000000000f05",  # GameBalance.cs
    "@@GUID_SCENE@@":     "7a1b0000000000000000000000000f06",  # Main.unity
    "@@GUID_WS_ASSET@@":  "7a1b0000000000000000000000000f07",  # WorldSettings.asset
    "@@GUID_GB_ASSET@@":  "7a1b0000000000000000000000000f08",  # GameBalance.asset
}

SUBSTITUTE_FILES = [
    "Assets/Scenes/Main.unity",
    "Assets/Ecosphere/Config/WorldSettings.asset",
    "Assets/Ecosphere/Config/GameBalance.asset",
    "ProjectSettings/EditorBuildSettings.asset",
]


def substitute_placeholders() -> None:
    for rel in SUBSTITUTE_FILES:
        path = os.path.join(ROOT, rel)
        with open(path, "r", encoding="utf-8") as f:
            text = f.read()
        original = text
        for placeholder, guid in FIXED.items():
            text = text.replace(placeholder, guid)
        if text != original:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            print(f"substituted: {rel}")


def new_guid() -> str:
    return uuid.uuid4().hex


def folder_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def default_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def script_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "MonoImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  defaultReferences: []\n"
        "  executionOrder: 0\n"
        "  icon: {instanceID: 0}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def asmdef_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "AssemblyDefinitionImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def scriptable_object_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 11400000\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def text_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def meta_for(path: str, guid: str) -> str:
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return script_meta(guid)
    if ext == ".asmdef":
        return asmdef_meta(guid)
    if ext == ".asset":
        return scriptable_object_meta(guid)
    if ext == ".unity":
        return default_meta(guid)
    if ext in {".md", ".txt", ".json"}:
        return text_meta(guid)
    return default_meta(guid)


def fixed_guid_for(rel_path: str):
    mapping = {
        os.path.join("Assets", "Ecosphere", "Authoring", "WorldBootstrap.cs"): FIXED["@@GUID_BOOTSTRAP@@"],
        os.path.join("Assets", "Ecosphere", "Presentation", "TimeControlInput.cs"): FIXED["@@GUID_INPUT@@"],
        os.path.join("Assets", "Ecosphere", "Presentation", "SimHud.cs"): FIXED["@@GUID_HUD@@"],
        os.path.join("Assets", "Ecosphere", "Authoring", "Config", "WorldSettings.cs"): FIXED["@@GUID_WS_CS@@"],
        os.path.join("Assets", "Ecosphere", "Authoring", "Config", "GameBalance.cs"): FIXED["@@GUID_GB_CS@@"],
        os.path.join("Assets", "Scenes", "Main.unity"): FIXED["@@GUID_SCENE@@"],
        os.path.join("Assets", "Ecosphere", "Config", "WorldSettings.asset"): FIXED["@@GUID_WS_ASSET@@"],
        os.path.join("Assets", "Ecosphere", "Config", "GameBalance.asset"): FIXED["@@GUID_GB_ASSET@@"],
    }
    return mapping.get(rel_path)


def generate_metas() -> None:
    created = 0
    for base, dirs, files in os.walk(ASSETS):
        dirs.sort()
        entries = [(d, True) for d in dirs] + [(f, False) for f in sorted(files)]
        for name, is_dir in entries:
            full = os.path.join(base, name)
            rel = os.path.relpath(full, ROOT)
            meta_path = full + ".meta"
            if os.path.exists(meta_path):
                continue
            guid = fixed_guid_for(rel) or new_guid()
            content = folder_meta(guid) if is_dir else meta_for(full, guid)
            with open(meta_path, "w", encoding="utf-8", newline="\n") as f:
                f.write(content)
            created += 1
            print(f"meta: {rel}.meta ({'dir' if is_dir else 'file'})")
    print(f"created {created} meta files")


def verify() -> None:
    missing = []
    for base, dirs, files in os.walk(ASSETS):
        for name in dirs + files:
            if name.endswith(".meta"):
                continue
            full = os.path.join(base, name)
            if not os.path.exists(full + ".meta"):
                missing.append(full)
    leftovers = []
    for base, _, files in os.walk(ROOT):
        if ".git" in base or "Library" in base:
            continue
        for f in files:
            if f.endswith((".unity", ".asset")):
                p = os.path.join(base, f)
                with open(p, "r", encoding="utf-8") as fh:
                    if "@@" in fh.read():
                        leftovers.append(p)
    assert not missing, f"missing metas: {missing}"
    assert not leftovers, f"unresolved placeholders: {leftovers}"
    print("verify: OK (all metas present, no placeholders left)")


if __name__ == "__main__":
    substitute_placeholders()
    generate_metas()
    verify()
