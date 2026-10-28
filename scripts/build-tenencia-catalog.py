"""Build the reviewed application data from the supplied Excel's lossless CSV transcription."""
import csv,json,re,sys
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
 if row==53:note.append('El título exige1 envase de25 y otro de50mcg/h; no se acredita sumando todos los fentanilos.')
 if row==83:note.append('El título exige CH16 y CH18; debe comprobarse cada tamaño.')
 if row==84:note.append('Discrepancia del Excel: título2 unidades, columna Ten. Mín.4. Se conserva4 pendiente de confirmación.')
 group=dict(id=f'malaga-{row:03d}',name=name,minimum=int(r['minimo']),codes=[code] if code else [],sourceRow=row,requiresReview=bool(note),selectionNote=' '.join(note) or None)
 if medical:group['atcPrefix']=r['clasificacion'].strip()
 elif code:group['descriptions']={code:name}
 else:group['classificationCode']=extra
 out.append(group)
assert [g['sourceRow'] for g in out]==list(range(2,85))
catalog=dict(schema=1,province='29',version='Malaga-Excel-2026-10-28',source='existencias minimas.xlsx aportado por el usuario; transcripción de83 grupos. No acredita revisión normativa oficial.',reviewedOn='2026-10-28',groups=out)
p=root/'src/Viteka.Server/Catalogs/tenencia/29.json';expected=json.dumps(catalog,ensure_ascii=False,indent=2)+'\n'
if '--check' in sys.argv:
 assert p.read_text(encoding='utf-8')==expected,'El catálogo no coincide con la transcripción CSV'
else:
 p.parent.mkdir(parents=True,exist_ok=True);p.write_text(expected,encoding='utf-8')
print(f'{len(out)} grupos; {sum(g["minimum"] for g in out)} unidades mínimas de columna C; {sum(bool(g.get("atcPrefix")) for g in out)} selectores ATC; {sum(bool(g.get("classificationCode")) for g in out)} clasificaciones sanitarias; {sum(len(g["codes"]) for g in out)} CN explícito.')
