"""Build the catalogue from the supplied Excel and video reference transcriptions."""
import csv,json,re,sys,collections
from pathlib import Path
root=Path(__file__).resolve().parents[1]
rows=list(csv.DictReader((root/'data/tenencia/malaga-excel.csv').open(encoding='utf-8')))
assert len(rows)==83
out=[]
for r in rows:
 row=int(r['fila_excel']);name=r['grupo'];medical=r['clasificacion'].strip()!='P, SANITARIOS';extra=r['codigo_adicional'].strip();code=extra if re.fullmatch(r'[0-9]{6,7}',extra) else None
 note=[]
 if medical and re.search(r'ORAL|INYECTABLE|INHALADOR|TÓPICO|PARCHES|COLIRIO|POMADA|MICROENEMAS|SUSPENSIÓN|PEDIÁTRICO',name):
  note.append('Verificar vía, forma y dosis del título: el ATC por sí solo no acredita esas restricciones.')
 if not medical and not code:note.append('Código de productos sanitarios de la columna D; pendiente de identificar la clasificación equivalente en NixFarma.')
 if row==53:note.append('El título exige 1 envase de 25 y otro de 50 mcg/h; no se acredita sumando todos los fentanilos.')
 if row==83:note.append('El título exige CH16 y CH18; debe comprobarse cada tamaño.')
 if row==84:note.append('Discrepancia del Excel: título2 unidades, columna Ten. Mín.4. Se conserva4 pendiente de confirmación.')
 group=dict(id=f'malaga-{row:03d}',name=name,minimum=int(r['minimo']),codes=[code] if code else [],sourceRow=row,requiresReview=bool(note),selectionNote=' '.join(note) or None)
 if medical:group['atcPrefix']=r['clasificacion'].strip()
 elif code:group['descriptions']={code:name}
 else:group['classificationCode']=extra
 out.append(group)
assert [g['sourceRow'] for g in out]==list(range(2,85))
# The original Excel remains unchanged. Video-derived associations and minima are a second, traceable source.
video_rows=list(csv.DictReader((root/'data/tenencia/malaga-video-articulos.csv').open(encoding='utf-8')))
video_meta=json.loads((root/'data/tenencia/malaga-video-fuentes.json').read_text(encoding='utf-8'))
def preview_name(reference):
 name=re.split(r'[0-9]',reference['denominacion_visible_parcial'],maxsplit=1)[0]
 name=re.sub(r'\s+',' ',name.replace('|',' ')).strip(' .,-…>_')
 return (name or 'Artículo CN '+reference['cn'])[:197]+'…'
by_group=collections.defaultdict(list)
for r in video_rows:
 assert re.fullmatch(r'[0-9]{6}',r['cn'])
 by_group[int(r['fila_excel'])].append(r)
assert len(video_rows)==2089 and set(by_group)==set(range(2,85))
meta={r['sourceRow']:r for r in video_meta['groups']}
for group in out:
 row=group['sourceRow'];refs=by_group[row]
 assert len(refs)==len({r['cn'] for r in refs})==meta[row]['codes']
 group['codes']=[r['cn'] for r in refs]
 # Only CNs were visually verified. Avoid presenting OCR dosage/packaging as authoritative.
 group['descriptions']={r['cn']:preview_name(r) for r in refs}
 group['minimum']=meta[row]['minimum']
 group['requiresReview']=row in (53,61,83)
 group['selectionNote']=None
 if row==53:group['selectionNote']='La referencia exige 1 envase de 25 y otro de 50 mcg/h. Se muestran los 30 medicamentos del vídeo; debe acreditarse cada dosis por separado.'
 if row==61:group['selectionNote']='El vídeo incluye el CN 718796 como VALIUM 10 mg 5 ampollas en este grupo de microenemas. Se conserva la asociación de la referencia, pendiente de confirmar su inclusión.'
 if row==83:group['selectionNote']='Se muestran las 31 referencias del vídeo. La condición CH16/CH18 debe comprobarse por tamaño, no solo mediante una suma global.'
 if row==80:group['selectionNote']='El vídeo muestra 18 referencias de bisturíes. El CN 123299 del Excel original no aparece en esa lista y se conserva únicamente en la transcripción de origen.'
 if row==84:group['selectionNote']='Mínimo corregido a 2 según la ficha visible en el vídeo; la columna C del Excel original indicaba 4.'
assert sum(g['minimum'] for g in out)==178
catalog=dict(schema=1,province='29',version='Malaga-Excel-Video-2026-10-28',source='Excel y vídeos de Unycop aportados por el usuario: 83 grupos y 2.089 asociaciones. Nombres abreviados de referencia; NixFarma aporta las denominaciones completas. No se importan existencias del vídeo ni se acredita revisión normativa oficial.',reviewedOn='2026-10-28',groups=out)
p=root/'src/Viteka.Server/Catalogs/tenencia/29.json';expected=json.dumps(catalog,ensure_ascii=False,indent=2)+'\n'
if '--check' in sys.argv:
 assert p.read_text(encoding='utf-8')==expected,'El catálogo no coincide con la transcripción CSV'
else:
 p.parent.mkdir(parents=True,exist_ok=True);p.write_text(expected,encoding='utf-8')
print(f'{len(out)} grupos; {sum(g["minimum"] for g in out)} mínimos de las referencias combinadas; {sum(bool(g.get("atcPrefix")) for g in out)} selectores ATC; {sum(bool(g.get("classificationCode")) for g in out)} clasificaciones sanitarias; {sum(len(g["codes"]) for g in out)} CN explícito.')
