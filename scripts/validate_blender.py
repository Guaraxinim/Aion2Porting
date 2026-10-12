"""Validate Aion2 GLB geometry in Blender and create a portable preview .blend.
Base color only: custom Aion shaders, fur, masks and normal conventions are not reconstructed.
Run: blender --background --factory-startup --python-exit-code 1 --python scripts/validate_blender.py -- exports/RUN
"""
import bpy
import sys
import os
import json
import math
from pathlib import Path

root = Path(os.environ.get("AION2_ROOT", Path(__file__).resolve().parent.parent)).resolve()
source = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
if not source.is_relative_to(root / 'exports'):
    raise ValueError('Validation input must be inside project exports')
records = []
for path in sorted(source.rglob('*.glb')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = bpy.ops.import_scene.gltf(filepath=str(path))
    rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    helpers = {p.custom_shape for rig in rigs for p in rig.pose.bones if p.custom_shape}
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o not in helpers]
    material_records = []
    for obj in meshes:
        for mat in obj.data.materials:
            if mat is None:
                continue
            candidates = list(source.rglob(mat.name + '.json'))
            if len(candidates) != 1:
                material_records.append({'material': mat.name, 'status': 'missing or ambiguous metadata'})
                continue
            meta = json.loads(candidates[0].read_text(encoding='utf-8'))
            textures = meta.get('Textures', {})
            ref = textures.get('BaseColor') or textures.get('PM_Diffuse')
            if not ref:
                continue
            relative = ref.split('.')[0].removeprefix('/Game/')
            png = source / 'Aion2' / 'Content' / (relative + '.png')
            if not png.is_file():
                material_records.append({'material': mat.name, 'status': 'missing base color', 'texture': str(png)})
                continue
            mat.use_nodes = True
            node = mat.node_tree.nodes.new('ShaderNodeTexImage')
            node.image = bpy.data.images.load(str(png), check_existing=True)
            bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
            mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
            node.image.pack()
            material_records.append({'material': mat.name, 'status': 'base color connected', 'texture': str(png)})
    data = {
        'file': str(path), 'import_result': list(result),
        'meshes': [{'name': o.name, 'vertices': len(o.data.vertices), 'polygons': len(o.data.polygons),
                    'uv_layers': len(o.data.uv_layers), 'vertex_groups': len(o.vertex_groups),
                    'vertices_with_weights': sum(any(g.weight > 0 for g in v.groups) for v in o.data.vertices),
                    'materials': [m.name if m else None for m in o.data.materials],
                    'finite_coordinates': all(math.isfinite(c) for v in o.data.vertices for c in v.co),
                    'dimensions': list(o.dimensions)} for o in meshes],
        'armatures': [{'name': o.name, 'bones': len(o.data.bones)} for o in rigs],
        'excluded_bone_shape_helpers': [o.name for o in helpers],
        'images': [{'name': i.name, 'size': list(i.size), 'has_data': i.has_data} for i in bpy.data.images],
        'material_setup': material_records,
        'material_fidelity': 'Base color preview only; game shader not reconstructed',
    }
    data['valid_geometry'] = bool(meshes) and all(m['vertices'] > 0 and m['polygons'] > 0 and m['finite_coordinates'] for m in data['meshes'])
    data['valid_skinning'] = bool(rigs) and all(m['vertices_with_weights'] == m['vertices'] for m in data['meshes'])
    if data['valid_geometry']:
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
    records.append(data)
report = root / 'reports' / ('blender-' + source.name + '.json')
report.write_text(json.dumps({'blender_version': bpy.app.version_string, 'models': records}, indent=2), encoding='utf-8')
print('VALIDATION_REPORT', report)
if not records or not all(r['valid_geometry'] for r in records):
    raise RuntimeError('No models or invalid geometry')
