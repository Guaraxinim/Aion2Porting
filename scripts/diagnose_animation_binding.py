import bpy
import json
import sys
from pathlib import Path

model, blend, report = sys.argv[sys.argv.index('--') + 1:]
def snapshot():
    rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    rows = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or not any(m.type == 'ARMATURE' for m in obj.modifiers):
            continue
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        points = [evaluated.matrix_world @ v.co for v in mesh.vertices]
        rows.append(dict(Name=obj.name, Parent=obj.parent.name if obj.parent else None,
                         World=[list(r) for r in obj.matrix_world],
                         Bounds=[[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]))
        evaluated.to_mesh_clear()
    return dict(Rigs=[dict(Name=r.name,World=[list(row) for row in r.matrix_world],Bones=len(r.data.bones)) for r in rigs],Meshes=rows)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=model)
original=snapshot()
bpy.ops.wm.open_mainfile(filepath=blend)
for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        obj.data.pose_position = 'REST'
bpy.context.view_layer.update()
exported=snapshot()
Path(report).write_text(json.dumps(dict(Original=original,ExportedRest=exported),indent=2),encoding='utf-8')
