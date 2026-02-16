import bpy
import random
import math

# -------------------------
# RESET SCENY
# -------------------------
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# -------------------------
# PARAMETRY
# -------------------------
TRUNK_HEIGHT = 4.0
TRUNK_RADIUS = 0.25
TRUNK_TAPER = 0.6

CANOPY_RADIUS = 1.8
CANOPY_HEIGHT = 2.2

SUBDIV_TRUNK = 2
SUBDIV_CANOPY = 1

# -------------------------
# PIEŃ
# -------------------------
bpy.ops.mesh.primitive_cylinder_add(
    vertices=12,
    radius=TRUNK_RADIUS,
    depth=TRUNK_HEIGHT,
    location=(0, 0, TRUNK_HEIGHT / 2)
)
trunk = bpy.context.active_object
trunk.name = "Tree_Trunk"

# Zwężenie pnia
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.transform.resize(
    value=(TRUNK_TAPER, TRUNK_TAPER, 1),
    orient_type='GLOBAL'
)
bpy.ops.object.mode_set(mode='OBJECT')

# Subdivision
sub = trunk.modifiers.new("Subsurf", 'SUBSURF')
sub.levels = SUBDIV_TRUNK

bpy.ops.object.shade_smooth()

# Materiał pnia
mat_trunk = bpy.data.materials.new("Mat_Trunk")
mat_trunk.use_nodes = True
nodes = mat_trunk.node_tree.nodes
bsdf = nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (0.25, 0.15, 0.07, 1)
bsdf.inputs["Roughness"].default_value = 0.8
trunk.data.materials.append(mat_trunk)

# -------------------------
# KORONA
# -------------------------
bpy.ops.mesh.primitive_ico_sphere_add(
    subdivisions=3,
    radius=CANOPY_RADIUS,
    location=(0, 0, TRUNK_HEIGHT + CANOPY_HEIGHT / 2)
)
canopy = bpy.context.active_object
canopy.name = "Tree_Canopy"

# Lekka deformacja (organiczność)
for v in canopy.data.vertices:
    v.co += v.normal * random.uniform(-0.15, 0.25)

# Subdivision
sub2 = canopy.modifiers.new("Subsurf", 'SUBSURF')
sub2.levels = SUBDIV_CANOPY

bpy.ops.object.shade_smooth()

# Materiał liści
mat_canopy = bpy.data.materials.new("Mat_Canopy")
mat_canopy.use_nodes = True
nodes = mat_canopy.node_tree.nodes
bsdf = nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (0.12, 0.35, 0.12, 1)
bsdf.inputs["Roughness"].default_value = 0.6
canopy.data.materials.append(mat_canopy)

# -------------------------
# ORIGIN + TRANSFORM
# -------------------------
bpy.ops.object.select_all(action='DESELECT')
trunk.select_set(True)
canopy.select_set(True)
bpy.context.view_layer.objects.active = trunk
bpy.ops.object.join()

tree = bpy.context.active_object
tree.name = "Tree_Model"

bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
tree.location = (0, 0, 0)

# Skala 1 = 1m (Unity)
tree.scale = (1, 1, 1)

print("Drzewo wygenerowane poprawnie.")
