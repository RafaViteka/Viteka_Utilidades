"""Build deterministic standalone Windows STDIO connector package (no credentials)."""
from pathlib import Path
import hashlib,io,sys,zipfile
root=Path(__file__).resolve().parents[1]
folder=root/'tools/nixfarma-mcp'
stream=io.BytesIO()
with zipfile.ZipFile(stream,'w',compression=zipfile.ZIP_STORED) as archive:
    for name in ('LEEME.md','instalar.ps1','requirements.txt','server.py'):
        info=zipfile.ZipInfo(name,date_time=(2026,10,30,0,0,0))
        info.compress_type=zipfile.ZIP_STORED
        info.create_system=3
        info.external_attr=0o100644 << 16
        archive.writestr(info,(folder/name).read_text(encoding='utf-8').encode('utf-8'))
result=stream.getvalue();path=root/'downloads/Viteka-NixFarma-MCP-0.1.1.zip'
digest=hashlib.sha256(result).hexdigest();hashpath=path.with_suffix('.zip.sha256');hashtext=digest+'  '+path.name+'\n'
if '--check' in sys.argv:
    assert path.read_bytes()==result and hashpath.read_text()==hashtext,'Regenerar paquete MCP'
else:
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(result);hashpath.write_text(hashtext)
print(path.name,len(result),'bytes',digest)
