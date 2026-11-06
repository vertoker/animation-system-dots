"""Generate a textured variant of the licensed Compute Deformation sample graph.

Uses node schemas from the installed Shader Graph package; no package files change.
"""
from copy import deepcopy
import json
from pathlib import Path
import shutil
import uuid

def read_graph(path):
    text = path.read_text(encoding='utf-8'); decoder = json.JSONDecoder(); i = 0; result = []
    while i < len(text):
        while i < len(text) and text[i].isspace(): i += 1
        if i == len(text): break
        value, i = decoder.raw_decode(text, i); result.append(value)
    return result

def uid(name): return uuid.uuid5(uuid.NAMESPACE_URL, 'animation-system-dots/humanoid/' + name).hex
def edge(output, output_slot, target, input_slot):
    return {'m_OutputSlot': {'m_Node': {'m_Id': output}, 'm_SlotId': output_slot},
            'm_InputSlot': {'m_Node': {'m_Id': target}, 'm_SlotId': input_slot}}

base = Path('Assets/AnimationSystemDOTS.Sandbox/Fixtures/Materials')
objects = read_graph(base / 'ComputeDeformation.shadergraph')
graph = next(o for o in objects if o['m_Type'].endswith('GraphData'))
category = next(o for o in objects if o['m_Type'].endswith('CategoryData'))
property_node = next(o for o in objects if o['m_Type'].endswith('PropertyNode'))
package = next(Path('Library/PackageCache').glob('com.unity.shadergraph@*'))
template = read_graph(package / 'GraphTemplates/Custom Render Texture.shadergraph')
sample = next(o for o in template if o['m_Type'].endswith('SampleTexture2DNode'))
sample_ids = [slot['m_Id'] for slot in sample['m_Slots']]
sample_objects = [sample] + [o for o in template if o.get('m_ObjectId') in sample_ids]
properties = read_graph(package / 'Samples~/CustomLighting/Custom Lighting Basic.shadergraph')
texture_property = next(o for o in properties if o['m_Type'].endswith('Texture2DShaderProperty'))
graph['m_Edges'] = [e for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id'] != 'bf7ae92451f74dc086a01269956633ae']

for name, reference, normal, target in [
    ('Base Map', '_BaseMap', False, 'bf7ae92451f74dc086a01269956633ae'),
    ('Normal Map', '_BumpMap', True, '1347834f0cfc40acb5d5cc1e2c8be070')]:
    prop = deepcopy(texture_property); prop['m_ObjectId'] = uid(reference + '/property')
    prop['m_Guid']['m_GuidSerialized'] = str(uuid.UUID(uid(reference + '/guid')))
    prop['m_Name'] = name; prop['m_DefaultReferenceName'] = reference; prop['m_OverrideReferenceName'] = reference
    prop['m_RefNameGeneratedByDisplayName'] = name; prop['m_DefaultType'] = 3 if normal else 0
    prop['isMainTexture'] = not normal; objects.append(prop)
    graph['m_Properties'].append({'m_Id': prop['m_ObjectId']}); category['m_ChildObjectList'].append({'m_Id': prop['m_ObjectId']})
    node = deepcopy(property_node); node['m_ObjectId'] = uid(reference + '/node'); node['m_Property']['m_Id'] = prop['m_ObjectId']
    slot_id = uid(reference + '/slot'); node['m_Slots'] = [{'m_Id': slot_id}]
    slot = {'m_SGVersion': 0, 'm_Type': 'UnityEditor.ShaderGraph.Texture2DMaterialSlot', 'm_ObjectId': slot_id,
            'm_Id': 0, 'm_DisplayName': name, 'm_SlotType': 1, 'm_Hidden': False,
            'm_ShaderOutputName': reference, 'm_StageCapability': 3, 'm_BareResource': False}
    cloned = deepcopy(sample_objects)
    remap = {o['m_ObjectId']: uid(reference + '/' + o['m_ObjectId']) for o in cloned}
    cloned = json.loads(json.dumps(cloned))
    for item in cloned:
        old = item['m_ObjectId']; item['m_ObjectId'] = remap[old]
        for child in item.get('m_Slots', []): child['m_Id'] = remap[child['m_Id']]
    sampler = cloned[0]; sampler['m_TextureType'] = 1 if normal else 0
    objects.extend([node, slot] + cloned)
    graph['m_Nodes'].extend([{'m_Id': node['m_ObjectId']}, {'m_Id': sampler['m_ObjectId']}])
    graph['m_Edges'].extend([edge(node['m_ObjectId'], 0, sampler['m_ObjectId'], 1), edge(sampler['m_ObjectId'], 0, target, 0)])

output = Path('Samples~/Sandbox/Humanoid/Materials'); output.mkdir(parents=True, exist_ok=True)
(output / 'HumanoidComputeDeformation.shadergraph').write_text('\n\n'.join(json.dumps(o, indent=4) for o in objects) + '\n', encoding='utf-8')
shutil.copy2(base / 'UnitySamplesLicense.txt', output / 'UnitySamplesLicense.txt')
print('Generated textured graph with native Compute Deformation position/normal/tangent wiring.')
