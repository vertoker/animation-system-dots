"""Run with Blender --background --python this_file -- --source DIR --output DIR.

Converts only the armature/action from four user-provided GLBs; originals stay intact.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--all-candidates', action='store_true')
parser.add_argument('--workout-blend', help='Optional cleaned sequence used by HumanMale_Workout_Preview_v2.mp4')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
source, output = Path(args.source), Path(args.output)
output.mkdir(parents=True, exist_ok=True)
manifest = []

def export_with_bind_pose(armature, destination, step=1):
    # An armature-only FBX has no skin clusters/BindPose. Unity then calibrates
    # its Avatar from the first animated frame. A tiny, non-runtime skin keeps
    # every bone's actual rest matrix in the FBX without copying source meshes.
    mesh = bpy.data.meshes.new('AvatarBindPoseMesh')
    vertices, triangles = [], []
    for bone in armature.data.bones:
        p = bone.head_local
        first = len(vertices)
        vertices.extend([tuple(p), (p.x + .001, p.y, p.z), (p.x, p.y + .001, p.z)])
        triangles.append((first, first + 1, first + 2))
    mesh.from_pydata(vertices, [], triangles)
    proxy = bpy.data.objects.new('AvatarBindPose', mesh)
    bpy.context.collection.objects.link(proxy)
    proxy.matrix_world = armature.matrix_world.copy()
    for index, bone in enumerate(armature.data.bones):
        proxy.vertex_groups.new(name=bone.name).add(list(range(index * 3, index * 3 + 3)), 1, 'REPLACE')
    proxy.modifiers.new('BindPoseSkin', 'ARMATURE').object = armature
    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True); proxy.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(filepath=str(destination), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, use_armature_deform_only=False,
        axis_forward='-Z', axis_up='Y', bake_anim=True, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0, bake_anim_step=step,
        use_mesh_modifiers=False, apply_scale_options='FBX_SCALE_ALL')

candidates = [(action, variant) for action in ('Walk', 'Jump', 'Punch', 'Squat') for variant in ('01', '02', '03')]
selected = [('Walk', '01'), ('Jump', '03'), ('Punch', '01'), ('Squat', '02')]
for action, variant in candidates if args.all_candidates else selected:
    origin = source / f'UniMate_{action}_Variant{variant}.glb'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=str(origin))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE']
    if len(armatures) != 1:
        raise RuntimeError(f'{origin}: expected one armature, got {len(armatures)}')
    armature = armatures[0]
    if armature.animation_data is None or armature.animation_data.action is None:
        raise RuntimeError(f'{origin}: no active animation action')
    action_range = armature.animation_data.action.frame_range
    bpy.context.scene.frame_start = int(round(action_range[0]))
    bpy.context.scene.frame_end = int(round(action_range[1]))
    destination = output / f'{action}{"" if variant == "01" else variant}.fbx'
    export_with_bind_pose(armature, destination)
    manifest.append({'source': str(origin), 'sha256': hashlib.sha256(origin.read_bytes()).hexdigest(),
                     'output': destination.name, 'blender': bpy.app.version_string,
                     'frameStart': bpy.context.scene.frame_start, 'frameEnd': bpy.context.scene.frame_end,
                     'fps': bpy.context.scene.render.fps, 'bones': len(armature.data.bones)})
if args.workout_blend:
    origin = Path(args.workout_blend)
    bpy.ops.wm.open_mainfile(filepath=str(origin))
    armature = bpy.data.objects['HumanMale_Rig']
    if armature.animation_data.action.name != 'HumanMale_Workout_Final':
        raise RuntimeError('Expected the final cleaned action matching the supplied preview')
    armature.hide_set(False); armature.hide_viewport = False
    destination = output / 'Workout.fbx'
    reference = []
    for time in (.137, 1.0, 3.5, 4.0, 7.5, 8.5, 10.3, 11.5):
        frame = 1 + time * bpy.context.scene.render.fps
        bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
        points = []
        for bone in armature.pose.bones:
            p = armature.matrix_world @ bone.head
            points.append({'name': bone.name, 'position': {'x': -p.x, 'y': p.z, 'z': -p.y}})
        reference.append({'time': time, 'bones': points})
    (output / 'Workout.reference.json').write_text(json.dumps({'frames': reference}, indent=2) + '\n', encoding='utf-8')
    export_with_bind_pose(armature, destination, bpy.context.scene.render.fps / 60)
    manifest.append({'source': str(origin), 'sha256': hashlib.sha256(origin.read_bytes()).hexdigest(),
        'output': destination.name, 'action': armature.animation_data.action.name,
        'frameStart': bpy.context.scene.frame_start, 'frameEnd': bpy.context.scene.frame_end,
        'fps': bpy.context.scene.render.fps, 'sampleFps': 60, 'bones': len(armature.data.bones),
        'blender': bpy.app.version_string})
(output / 'ConversionManifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print('ANIMATION_CONVERSION_COMPLETE', json.dumps(manifest))
