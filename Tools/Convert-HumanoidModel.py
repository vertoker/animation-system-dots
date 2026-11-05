"""Blender background export of the original bald male, normalized for Unity Humanoid."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
source, output = Path(args.source), Path(args.output)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True,
    object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, use_armature_deform_only=False,
    axis_forward='-Z', axis_up='Y', bake_anim=False, use_mesh_modifiers=False,
    path_mode='STRIP', apply_scale_options='FBX_SCALE_ALL')
output.with_suffix('.provenance.json').write_text(json.dumps({
    'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'blender': bpy.app.version_string, 'output': output.name,
    'purpose': 'Same original geometry and bind pose; consistent +Z-facing FBX for Unity Humanoid'
}, indent=2) + '\n', encoding='utf-8')
