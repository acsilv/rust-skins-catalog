#!/usr/bin/env python3
"""Preenche o NunusDecor.json com shortnames verificados (Rust PC, 2026).
Não modifica plugins, não mexe nas licenças/DLC, não reinicia o Rust.
"""
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

PATH = Path('/home/rust/server/oxide/config/NunusDecor.json')

# ID existente -> (shortname, nome mais preciso se necessário)
ITEMS = {
 'frontier_hunting_trophy': ('huntingtrophysmall', 'Small Hunting Trophy'),
 'frontier_fish_trophy': ('fishtrophy', None),
 'frontier_bar_doors': ('door.double.hinged.bardoors', None),
 'frontier_frontier_planters': ('bathtub.planter', 'Bath Tub Planter'),
 'frontier_triangle_planter': ('triangle.rail.road.planter', 'Triangle Rail Road Planter'),
 'frontier_rocking_chair': ('rockingchair', None),
 'frontier_weapon_rack': ('gunrack.single.1.horizontal', 'Frontier Bolts Single Item Rack'),
 'frontier_storage_barrels': ('storage_barrel_b', 'Storage Barrel Vertical'),
 'frontier_torch_holder': ('torchholder', None),
 'frontier_skinning_knife': ('knife.skinning', None),
 'exhibit_clothing_mannequin': ('mannequin', None),
 'exhibit_wall_cabinet': ('electric.wallcabinet', None),
 'exhibit_fabric_beanbag_seat': ('beanbagseatfabric', None),
 'exhibit_chandelier': ('electric.chandelier', None),
 'exhibit_fairy_lights': ('electric.fairylights', None),
 'exhibit_bulb_string_lights': ('electric.bulbstringlights', None),
 'exhibit_spot_light': ('electric.spotlight', None),
 'exhibit_tripod_spot_light': ('electric.spotlight.tripod', None),
 'exhibit_fluorescent_light': ('electric.fluorescentlight', None),
 'exhibit_ceiling_fluorescent_light': ('electric.fluorescentlight.ceiling', None),
 'bargames_pool_table': ('pooltable', None),
 'bargames_darts_board': ('dartboard', None),
 'bargames_jukebox': ('jukebox', None),
 'bargames_mini_fridge': ('gamesroom.minifridge', 'Bar Games Minifridge'),
 'bargames_bar_stools': ('bar.stool.wood', 'Wooden Bar Stool'),
 'bargames_shotgun_trap': ('gamesroom.shotgun.trap', 'Bar Games Shotgun Trap'),
 'bargames_wallpaper': ('wallpaper.tool', 'Wallpaper Tool (skins separadas)'),
 'bargames_floor_wallpaper': ('wallpaper.flooring', 'Flooring (tema depende da skin)'),
 'bargames_sprays': ('spraycan', 'Spray Can (skins separadas)'),
}

# Variantes reais ausentes da lista inicial
EXTRA = [
 ('frontier_hunting_trophy_large','Frontier','Large Hunting Trophy','huntingtrophylarge','Trophy'),
 ('frontier_planter_minecart','Frontier','Minecart Planter','minecart.planter','Gardening'),
 ('frontier_planter_railroad','Frontier','Rail Road Planter','rail.road.planter','Gardening'),
 ('frontier_planter_bathtub','Frontier','Bath Tub Planter','bathtub.planter','Gardening'),
 ('frontier_rack_horseshoe','Frontier','Frontier Horseshoe Single Item Rack','gunrack.single.2.horizontal','Storage'),
 ('frontier_rack_horns','Frontier','Frontier Horns Single Item Rack','gunrack.single.3.horizontal','Storage'),
 ('frontier_barrel_horizontal','Frontier','Storage Barrel Horizontal','storage_barrel_c','Storage'),
 ('bargames_stool_metal','BarGames','Metal Bar Stool','bar.stool.metal','Furniture'),
 ('exhibit_leather_beanbag','Exhibit','Leather Beanbag Seat','beanbagseatleather','Furniture'),
]

if not PATH.is_file():
    raise SystemExit('Não achei o NunusDecor.json: ' + str(PATH))
data = json.loads(PATH.read_text(encoding='utf-8'))
if not isinstance(data,dict) or not isinstance(data.get('Items'),list):
    raise SystemExit('Estrutura inesperada: não alterei o arquivo')
backup = PATH.with_name(PATH.name + '.bak-' + datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S'))
shutil.copy2(PATH,backup)
seen=set(); updated=[]; pending=[]
for item in data['Items']:
    ident=item.get('Id')
    if not ident or ident in seen: continue
    seen.add(ident)
    if ident in ITEMS:
        short, rename = ITEMS[ident]
        item['ItemShortname']=short
        item['SkinId']=0
        if rename: item['Name']=rename
        updated.append((ident,short))
    else:
        pending.append(ident)
for ident,pack,name,short,category in EXTRA:
    if ident in seen: continue
    data['Items'].append({'Id':ident,'Pack':pack,'Name':name,'Category':category,
         'Description':'Item decorativo do Rust', 'ItemShortname':short,
         'SkinId':0,'PreviewImageKey':None,'PreviewImageUrl':None,
         'AdminOnly':True,'Commands':[]})
    updated.append((ident,short))
    seen.add(ident)
# Verificação estrita de todos os dados antes de escrever
for item in data['Items']:
    assert isinstance(item.get('Id'),str)
    if item['Id'] in dict(updated):
        assert isinstance(item.get('ItemShortname'),str) and item['ItemShortname']
tmp=PATH.with_suffix('.json.tmp')
tmp.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
json.loads(tmp.read_text(encoding='utf-8'))
tmp.replace(PATH)
print('Backup:',backup)
print('Itens mapeados/adicionados:',len(updated))
print('Total de entradas:',len(data['Items']))
print('Sem mapeamento:',len(pending),', '.join(pending))
print('ATENÇÃO: mapeamento externo; o plugin precisa validar ItemManager.FindItemDefinition no servidor.')
print('Os temas/skins de DLC exigem licença e nem toda variante é item independente.')
