import bpy
import sys
from mathutils import Vector

blend, image = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene
scene.frame_set(1)
points=[]
for obj in scene.objects:
    if obj.type!='MESH' or not any(m.type=='ARMATURE' for m in obj.modifiers):
        continue
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh=evaluated.to_mesh()
    points.extend(evaluated.matrix_world @ v.co for v in mesh.vertices)
    evaluated.to_mesh_clear()
low=Vector([min(p[i] for p in points) for i in range(3)])
high=Vector([max(p[i] for p in points) for i in range(3)])
center=(high+low)*.5
size=max(high-low)
bpy.ops.object.camera_add(location=center+Vector((size*.6,-size*1.5,size*.65)))
camera=bpy.context.object
camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'
camera.data.ortho_scale=size*1.4
scene.camera=camera
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='SINGLE'
scene.display.shading.single_color=(.5,.6,.7)
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD'
if scene.world is None:
    scene.world=bpy.data.worlds.new('Preview')
scene.world.color=(.025,.025,.025)
scene.render.resolution_x=800
scene.render.resolution_y=700
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=image
bpy.ops.render.render(write_still=True)
