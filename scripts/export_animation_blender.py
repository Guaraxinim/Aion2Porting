"""Create and reopen a Blender armature/action from a decoded Aion2 preview clip."""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index('--') + 1:]
source, output = map(lambda p: Path(p).resolve(), args[:2])
model = Path(args[2]).resolve() if len(args) > 2 and args[2] else None
asset_root = Path(args[3]).resolve() if len(args) > 3 and args[3] else (model.parent if model else None)
root = Path(__file__).resolve().parent.parent
if not output.is_relative_to(root / 'exports'):
    raise ValueError('Output must be inside project exports')
if source.stat().st_size > 64 * 1024 * 1024:
    raise ValueError('Clip exceeds 64 MB')
clip = json.loads(source.read_text(encoding='utf-8-sig'))
bones, frames = clip['Bones'], clip['Frames']
if not 1 <= len(bones) <= 512 or not 1 <= len(frames) <= 1800 or not 1 <= clip['Fps'] <= 60:
    raise ValueError('Invalid clip limits')
if len({b['Name'] for b in bones}) != len(bones):
    raise ValueError('Duplicate bone names')
for i, bone in enumerate(bones):
    if not -1 <= bone['Parent'] < i:
        raise ValueError('Invalid hierarchy')
for transforms in [[b['Rest'] for b in bones], *frames]:
    if len(transforms) != len(bones):
        raise ValueError('Invalid bone count')
    for t in transforms:
        if len(t) != 10 or not all(math.isfinite(v) for v in t) or sum(v*v for v in t[3:7]) < 1e-12:
            raise ValueError('Invalid transform')

# The decoder uses glTF Y-up meters. Blender uses Z-up meters.
axis = Matrix.Rotation(math.pi / 2, 4, 'X')
def world(transforms):
    result = []
    for i, t in enumerate(transforms):
        local = Matrix.LocRotScale(Vector(t[:3]), Quaternion((t[6], t[3], t[4], t[5])).normalized(), Vector(t[7:10]))
        parent = bones[i]['Parent']
        result.append(local if parent < 0 else result[parent] @ local)
    return [axis @ m for m in result]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = clip['Fps']
scene.frame_start, scene.frame_end = 1, len(frames)
scene.unit_settings.system = 'METRIC'
rest = world([b['Rest'] for b in bones])
imported_rig = None
bind_world = {}
mesh_reference = []
if model:
    if model.suffix.lower() != '.glb' or model.stat().st_size > 256 * 1024 * 1024:
        raise ValueError('Invalid GLB model')
    bpy.ops.import_scene.gltf(filepath=str(model))
    expected_names = {b['Name'] for b in bones}
    candidates = [o for o in scene.objects if o.type == 'ARMATURE' and set(o.data.bones.keys()).issubset(expected_names)]
    if len(candidates) != 1:
        raise ValueError('The GLB does not contain exactly one armature compatible with the animation')
    imported_rig = candidates[0]
    index = {b['Name']: i for i, b in enumerate(bones)}
    bind_world = {b.name: imported_rig.matrix_world @ b.matrix_local for b in imported_rig.data.bones}
    for obj in scene.objects:
        if obj.type != 'MESH' or not any(m.type == 'ARMATURE' and m.object == imported_rig for m in obj.modifiers):
            continue
        groups = {g.index: g.name for g in obj.vertex_groups}
        rows = []
        for vertex in obj.data.vertices:
            weights = [(groups[g.group], g.weight) for g in vertex.groups
                       if g.weight > 0 and groups[g.group] in bind_world]
            total = sum(w for _, w in weights)
            if total <= 0:
                raise ValueError('The GLB contains a vertex without valid bone weights')
            rows.append((obj.matrix_world @ vertex.co, [(index[n], w / total) for n, w in weights]))
        mesh_reference.append((obj.name, rows))
    if not mesh_reference:
        raise ValueError('The GLB has no mesh bound to the compatible armature')
    # Reject an incompatible rest pose instead of silently producing a distorted model.
    max_rest_error = max(max(abs((imported_rig.matrix_world @ b.matrix_local)[r][c] - rest[index[b.name]][r][c]) for r in range(4) for c in range(4)) for b in imported_rig.data.bones)
    if max_rest_error > .025:
        raise ValueError(f'GLB rest pose is incompatible: {max_rest_error}')
    # Pack images already referenced by the GLB. External Aion base colors are linked below when metadata exists.
    if asset_root and asset_root.is_dir():
        metadata = {}
        for path in asset_root.rglob('*.json'):
            metadata.setdefault(path.stem.casefold(), []).append(path)
        for obj in (o for o in scene.objects if o.type == 'MESH'):
            for mat in (m for m in obj.data.materials if m):
                matches = metadata.get(mat.name.casefold(), [])
                if len(matches) != 1:
                    continue
                try:
                    meta = json.loads(matches[0].read_text(encoding='utf-8-sig'))
                    ref = meta.get('Textures', {}).get('BaseColor') or meta.get('Textures', {}).get('PM_Diffuse')
                    if not ref or not ref.startswith('/Game/'):
                        continue
                    png = asset_root / 'Aion2' / 'Content' / (ref.split('.')[0][6:] + '.png')
                    if not png.is_file():
                        continue
                    mat.use_nodes = True
                    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
                    node = mat.node_tree.nodes.new('ShaderNodeTexImage')
                    node.image = bpy.data.images.load(str(png), check_existing=True)
                    node.image.pack()
                    mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
                except Exception:
                    continue
    for image in bpy.data.images:
        if image.source == 'FILE' and image.filepath:
            try: image.pack()
            except RuntimeError: pass
if imported_rig:
    rig = imported_rig
    data = rig.data
else:
    data = bpy.data.armatures.new(clip['Skeleton'])
    rig = bpy.data.objects.new(clip['Skeleton'], data)
    scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
rig.show_in_front = True
bpy.ops.object.mode_set(mode='EDIT')
created = []
for i, bone in enumerate(bones):
    edit = data.edit_bones.get(bone['Name'])
    if edit is not None:
        created.append(edit)
        continue
    edit = data.edit_bones.new(bone['Name'])
    if edit.name != bone['Name']:
        raise ValueError('Blender changed a bone name; cannot preserve this skeleton')
    edit.matrix = rig.matrix_world.inverted() @ rest[i]
    edit.length = .03
    if bone['Parent'] >= 0:
        edit.parent = created[bone['Parent']]
    edit.use_connect = False
    created.append(edit)
# Keep existing GLB bone matrices, object parents and modifier targets. Changing
# those bind transforms can distort an otherwise correct set of bone poses.
for i, bone in enumerate(bones):
    created[i].parent = None if bone['Parent'] < 0 else created[bone['Parent']]
    created[i].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
bind_world.update({b.name: rig.matrix_world @ b.matrix_local for b in rig.data.bones})
rest_inverse = [m.inverted() for m in rest]
def target_world(frame):
    # Apply movement relative to the source rest pose to the actual GLB bind pose.
    return [m @ rest_inverse[i] @ bind_world[bones[i]['Name']] for i, m in enumerate(world(frame))]
rig_inverse = rig.matrix_world.inverted()
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
rig.animation_data_create()
action = bpy.data.actions.new(clip['Name'])
action.use_fake_user = True
rig.animation_data.action = action
previous = {}
for frame_number, frame in enumerate(frames, 1):
    matrices = [rig_inverse @ m for m in target_world(frame)]
    for i, bone in enumerate(bones):
        pb = rig.pose.bones[bone['Name']]
        parent = bone['Parent']
        kwargs = {} if parent < 0 else dict(parent_matrix=matrices[parent], parent_matrix_local=pb.parent.bone.matrix_local)
        pb.matrix_basis = pb.bone.convert_local_to_pose(matrices[i], pb.bone.matrix_local, invert=True, **kwargs)
        pb.rotation_mode = 'QUATERNION'
        q = pb.rotation_quaternion.copy()
        if pb.name in previous and q.dot(previous[pb.name]) < 0:
            q.negate()
            pb.rotation_quaternion = q
        previous[pb.name] = q.copy()
        for prop in ('location', 'rotation_quaternion', 'scale'):
            pb.keyframe_insert(data_path=prop, frame=frame_number, group=pb.name)
bpy.ops.object.mode_set(mode='OBJECT')
rig['aion2_animation'] = clip['Name']
rig['aion2_sample_fps'] = clip['Fps']
rig['aion2_note'] = 'Sampled animation; no physics, retargeting, notifies or game logic.'
rig['aion2_bind_pose_preserved'] = bool(model)
scene.frame_set(1)
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(output))

# Validate the persisted action, not just the scene before saving.
bpy.ops.wm.open_mainfile(filepath=str(output))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
if len(rig.data.bones) != len(bones) or not rig.animation_data or not rig.animation_data.action:
    raise RuntimeError('Missing saved armature or action')
max_error = 0.0
for number, frame in enumerate(frames, 1):
    scene.frame_set(number)
    expected = target_world(frame)
    for i, bone in enumerate(bones):
        pb = rig.pose.bones[bone['Name']]
        parent = pb.parent.name if pb.parent else None
        if parent != (bones[bone['Parent']]['Name'] if bone['Parent'] >= 0 else None):
            raise RuntimeError('Hierarchy changed')
        actual = rig.matrix_world @ pb.matrix
        max_error = max(max_error, max(abs(actual[r][c] - expected[i][r][c]) for r in range(4) for c in range(4)))
if max_error > .001:
    raise RuntimeError(f'Saved poses differ from source: {max_error}')
max_vertex_error = 0.0
checked_vertices = 0
checked_frames = sorted({0, len(frames)//4, len(frames)//2, 3*len(frames)//4, len(frames)-1})
for frame_index in checked_frames:
    scene.frame_set(frame_index + 1)
    delta = [m @ rest_inverse[i] for i, m in enumerate(world(frames[frame_index]))]
    for name, rows in mesh_reference:
        obj = scene.objects[name]
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        evaluated_mesh = evaluated.to_mesh()
        try:
            if len(evaluated_mesh.vertices) != len(rows):
                raise RuntimeError('Mesh vertex count changed during animation export')
            for vertex, (original, weights) in zip(evaluated_mesh.vertices, rows):
                expected_vertex = sum((delta[i] @ original * w for i, w in weights), Vector((0, 0, 0)))
                actual_vertex = evaluated.matrix_world @ vertex.co
                max_vertex_error = max(max_vertex_error, (actual_vertex - expected_vertex).length)
                checked_vertices += 1
        finally:
            evaluated.to_mesh_clear()
if max_vertex_error > .0002:
    raise RuntimeError(f'Saved mesh deformation differs from weighted source movement: {max_vertex_error} m')
meshes = [o for o in scene.objects if o.type == 'MESH']
weighted = sum(1 for o in meshes if any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers))
if model and (not meshes or not weighted):
    raise RuntimeError('Saved GLB meshes are not bound to the animated armature')
report = dict(Success=True, Skeleton=clip['Skeleton'], Animation=clip['Name'], Bones=len(bones), Frames=len(frames), Fps=clip['Fps'], MaxPoseError=max_error, MaxVertexErrorMeters=max_vertex_error, CheckedVertices=checked_vertices, CheckedMeshFrames=len(checked_frames) if model else 0, BindPosePreserved=bool(model), Reopened=True, Blender=bpy.app.version_string, ModelIncluded=bool(model), Meshes=len(meshes), WeightedMeshes=weighted, PackedImages=sum(1 for i in bpy.data.images if i.packed_file))
output.with_suffix('.validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
